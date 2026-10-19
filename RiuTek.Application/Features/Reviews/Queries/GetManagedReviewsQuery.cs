using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Common.Models;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Reviews.Common;
using RiuTek.Core.Common;

namespace RiuTek.Application.Features.Reviews.Queries;

public record GetManagedReviewsQuery(
    int PageIndex = 1,
    int PageSize = 20,
    Guid? ProductId = null,
    int? Rating = null,
    bool? HasReply = null
) : IRequest<Result<PagedResult<ManagedReviewDto>>>;

public class GetManagedReviewsQueryValidator : AbstractValidator<GetManagedReviewsQuery>
{
    public GetManagedReviewsQueryValidator()
    {
        RuleFor(x => x.PageIndex)
            .GreaterThanOrEqualTo(1).WithMessage("PageIndex must be greater than or equal to 1.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 50).WithMessage("PageSize must be between 1 and 50.");

        RuleFor(x => x.Rating)
            .InclusiveBetween(1, 5).WithMessage("Rating must be between 1 and 5.")
            .When(x => x.Rating.HasValue);
    }
}

public class GetManagedReviewsQueryHandler : IRequestHandler<GetManagedReviewsQuery, Result<PagedResult<ManagedReviewDto>>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public GetManagedReviewsQueryHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result<PagedResult<ManagedReviewDto>>> Handle(
        GetManagedReviewsQuery request,
        CancellationToken cancellationToken)
    {
        var authResult = await ReviewAuthHelper.ValidateActiveContentManagerAsync(
            _currentUserService,
            _context,
            cancellationToken);

        if (authResult.IsFailure)
        {
            return Result.Failure<PagedResult<ManagedReviewDto>>(authResult.Error);
        }

        var query = _context.Reviews.AsNoTracking();

        if (request.ProductId.HasValue)
        {
            query = query.Where(r => r.ProductId == request.ProductId.Value);
        }

        if (request.Rating.HasValue)
        {
            query = query.Where(r => r.Rating == request.Rating.Value);
        }

        if (request.HasReply.HasValue)
        {
            query = request.HasReply.Value
                ? query.Where(r => r.StaffReply != null)
                : query.Where(r => r.StaffReply == null);
        }

        int totalCount = await query.CountAsync(cancellationToken);
        long offset = ((long)request.PageIndex - 1) * request.PageSize;

        List<ManagedReviewDto> items;
        if (totalCount == 0 || offset >= totalCount || offset > int.MaxValue)
        {
            items = [];
        }
        else
        {
            items = await query
                .OrderByDescending(r => r.CreatedAt)
                .ThenByDescending(r => r.Id)
                .Skip((int)offset)
                .Take(request.PageSize)
                .Select(r => new ManagedReviewDto(
                    r.Id,
                    r.ProductId,
                    r.Product.Name,
                    r.Product.Sku,
                    r.UserId,
                    r.User.FullName,
                    r.OrderId,
                    r.Rating,
                    r.Title,
                    r.Content,
                    r.Images,
                    r.StaffReply,
                    r.StaffReplyAt,
                    r.CreatedAt,
                    r.UpdatedAt
                ))
                .ToListAsync(cancellationToken);
        }

        var paged = PagedResult<ManagedReviewDto>.Create(items, totalCount, request.PageIndex, request.PageSize);
        return Result.Success(paged);
    }
}

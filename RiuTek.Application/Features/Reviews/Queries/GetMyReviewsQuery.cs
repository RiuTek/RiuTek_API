using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Common.Models;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Reviews.Common;
using RiuTek.Core.Common;

namespace RiuTek.Application.Features.Reviews.Queries;

public record GetMyReviewsQuery(
    int PageIndex = 1,
    int PageSize = 20
) : IRequest<Result<PagedResult<MyReviewDto>>>;

public class GetMyReviewsQueryValidator : AbstractValidator<GetMyReviewsQuery>
{
    public GetMyReviewsQueryValidator()
    {
        RuleFor(x => x.PageIndex)
            .GreaterThanOrEqualTo(1).WithMessage("PageIndex must be greater than or equal to 1.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 50).WithMessage("PageSize must be between 1 and 50.");
    }
}

public class GetMyReviewsQueryHandler : IRequestHandler<GetMyReviewsQuery, Result<PagedResult<MyReviewDto>>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public GetMyReviewsQueryHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result<PagedResult<MyReviewDto>>> Handle(
        GetMyReviewsQuery request,
        CancellationToken cancellationToken)
    {
        var authResult = await ReviewAuthHelper.ValidateActiveCustomerAsync(
            _currentUserService,
            _context,
            cancellationToken);

        if (authResult.IsFailure)
        {
            return Result.Failure<PagedResult<MyReviewDto>>(authResult.Error);
        }

        var customer = authResult.Value;

        var query = _context.Reviews
            .AsNoTracking()
            .Where(r => r.UserId == customer.Id);

        int totalCount = await query.CountAsync(cancellationToken);
        long offset = ((long)request.PageIndex - 1) * request.PageSize;

        List<MyReviewDto> items;
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
                .Select(r => new MyReviewDto(
                    r.Id,
                    r.ProductId,
                    r.Product.Name,
                    r.Product.Slug,
                    r.Product.ImageUrl,
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

        var paged = PagedResult<MyReviewDto>.Create(items, totalCount, request.PageIndex, request.PageSize);
        return Result.Success(paged);
    }
}

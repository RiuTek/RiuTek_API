using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Common.Models;
using RiuTek.Application.DTOs;
using RiuTek.Core.Common;

namespace RiuTek.Application.Features.Reviews.Queries;

public record GetProductReviewsQuery(
    Guid ProductId,
    int PageIndex = 1,
    int PageSize = 10,
    int? Rating = null
) : IRequest<Result<ProductReviewsResultDto>>;

public class GetProductReviewsQueryValidator : AbstractValidator<GetProductReviewsQuery>
{
    public GetProductReviewsQueryValidator()
    {
        RuleFor(x => x.ProductId)
            .NotEmpty().WithMessage("ProductId is required.");

        RuleFor(x => x.PageIndex)
            .GreaterThanOrEqualTo(1).WithMessage("PageIndex must be greater than or equal to 1.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 50).WithMessage("PageSize must be between 1 and 50.");

        RuleFor(x => x.Rating)
            .InclusiveBetween(1, 5).WithMessage("Rating must be between 1 and 5.")
            .When(x => x.Rating.HasValue);
    }
}

public class GetProductReviewsQueryHandler : IRequestHandler<GetProductReviewsQuery, Result<ProductReviewsResultDto>>
{
    private readonly IApplicationDbContext _context;

    public GetProductReviewsQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<ProductReviewsResultDto>> Handle(
        GetProductReviewsQuery request,
        CancellationToken cancellationToken)
    {
        var productExists = await _context.Products
            .AsNoTracking()
            .AnyAsync(p => p.Id == request.ProductId, cancellationToken);

        if (!productExists)
        {
            return Result.Failure<ProductReviewsResultDto>(Error.NotFound(
                "Review.ProductNotFound",
                "Product was not found."));
        }

        var ratingStats = await _context.Reviews
            .AsNoTracking()
            .Where(r => r.ProductId == request.ProductId)
            .GroupBy(r => r.Rating)
            .Select(g => new { Rating = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        int totalReviews = ratingStats.Sum(x => x.Count);
        int oneStar = ratingStats.FirstOrDefault(x => x.Rating == 1)?.Count ?? 0;
        int twoStar = ratingStats.FirstOrDefault(x => x.Rating == 2)?.Count ?? 0;
        int threeStar = ratingStats.FirstOrDefault(x => x.Rating == 3)?.Count ?? 0;
        int fourStar = ratingStats.FirstOrDefault(x => x.Rating == 4)?.Count ?? 0;
        int fiveStar = ratingStats.FirstOrDefault(x => x.Rating == 5)?.Count ?? 0;

        decimal averageRating = totalReviews == 0
            ? 0m
            : Math.Round((decimal)ratingStats.Sum(x => (long)x.Rating * x.Count) / totalReviews, 2, MidpointRounding.AwayFromZero);

        var query = _context.Reviews
            .AsNoTracking()
            .Where(r => r.ProductId == request.ProductId);

        if (request.Rating.HasValue)
        {
            query = query.Where(r => r.Rating == request.Rating.Value);
        }

        int filteredCount = await query.CountAsync(cancellationToken);
        long offset = ((long)request.PageIndex - 1) * request.PageSize;

        List<ProductReviewDto> items;
        if (filteredCount == 0 || offset >= filteredCount || offset > int.MaxValue)
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
                .Select(r => new ProductReviewDto(
                    r.Id,
                    r.User.FullName,
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

        var paged = PagedResult<ProductReviewDto>.Create(items, filteredCount, request.PageIndex, request.PageSize);

        return Result.Success(new ProductReviewsResultDto(
            request.ProductId,
            averageRating,
            totalReviews,
            new RatingDistributionDto(fiveStar, fourStar, threeStar, twoStar, oneStar),
            paged
        ));
    }
}

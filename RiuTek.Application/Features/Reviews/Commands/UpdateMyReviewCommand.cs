using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Reviews.Common;
using RiuTek.Core.Common;

namespace RiuTek.Application.Features.Reviews.Commands;

public record UpdateMyReviewCommand(
    Guid ReviewId,
    int Rating,
    string? Title = null,
    string? Content = null
) : IRequest<Result<MyReviewDto>>;

public class UpdateMyReviewCommandValidator : AbstractValidator<UpdateMyReviewCommand>
{
    public UpdateMyReviewCommandValidator()
    {
        RuleFor(x => x.ReviewId)
            .NotEmpty().WithMessage("ReviewId is required.");

        RuleFor(x => x.Rating)
            .InclusiveBetween(1, 5).WithMessage("Rating must be between 1 and 5.");

        RuleFor(x => x.Title)
            .MaximumLength(200).WithMessage("Title cannot exceed 200 characters.")
            .When(x => !string.IsNullOrEmpty(x.Title));

        RuleFor(x => x.Content)
            .MaximumLength(2000).WithMessage("Content cannot exceed 2000 characters.")
            .When(x => !string.IsNullOrEmpty(x.Content));
    }
}

public class UpdateMyReviewCommandHandler : IRequestHandler<UpdateMyReviewCommand, Result<MyReviewDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public UpdateMyReviewCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result<MyReviewDto>> Handle(
        UpdateMyReviewCommand request,
        CancellationToken cancellationToken)
    {
        var authResult = await ReviewAuthHelper.ValidateActiveCustomerAsync(
            _currentUserService,
            _context,
            cancellationToken);

        if (authResult.IsFailure)
        {
            return Result.Failure<MyReviewDto>(authResult.Error);
        }

        var customer = authResult.Value;

        var review = await _context.Reviews
            .Include(r => r.Product)
            .FirstOrDefaultAsync(r => r.Id == request.ReviewId && r.UserId == customer.Id, cancellationToken);

        if (review is null)
        {
            return Result.Failure<MyReviewDto>(Error.NotFound(
                "Review.NotFound",
                "Review was not found."));
        }

        var updateResult = review.UpdateReview(request.Rating, request.Title, request.Content);
        if (updateResult.IsFailure)
        {
            return Result.Failure<MyReviewDto>(updateResult.Error);
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            _context.ClearTrackedChanges();
            return Result.Failure<MyReviewDto>(Error.Conflict(
                "Review.ConcurrencyConflict",
                "The review was modified concurrently."));
        }

        return Result.Success(new MyReviewDto(
            review.Id,
            review.Product.Id,
            review.Product.Name,
            review.Product.Slug,
            review.Product.ImageUrl,
            review.OrderId,
            review.Rating,
            review.Title,
            review.Content,
            review.Images,
            review.StaffReply,
            review.StaffReplyAt,
            review.CreatedAt,
            review.UpdatedAt
        ));
    }
}

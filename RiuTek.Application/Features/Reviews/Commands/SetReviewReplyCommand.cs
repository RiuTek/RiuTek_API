using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Reviews.Common;
using RiuTek.Core.Common;

namespace RiuTek.Application.Features.Reviews.Commands;

public record SetReviewReplyCommand(
    Guid ReviewId,
    string Reply
) : IRequest<Result<ManagedReviewDto>>;

public class SetReviewReplyCommandValidator : AbstractValidator<SetReviewReplyCommand>
{
    public SetReviewReplyCommandValidator()
    {
        RuleFor(x => x.ReviewId)
            .NotEmpty().WithMessage("ReviewId is required.");

        RuleFor(x => x.Reply)
            .NotEmpty().WithMessage("Reply cannot be empty.")
            .MaximumLength(2000).WithMessage("Reply cannot exceed 2000 characters.");
    }
}

public class SetReviewReplyCommandHandler : IRequestHandler<SetReviewReplyCommand, Result<ManagedReviewDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public SetReviewReplyCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result<ManagedReviewDto>> Handle(
        SetReviewReplyCommand request,
        CancellationToken cancellationToken)
    {
        var authResult = await ReviewAuthHelper.ValidateActiveContentManagerAsync(
            _currentUserService,
            _context,
            cancellationToken);

        if (authResult.IsFailure)
        {
            return Result.Failure<ManagedReviewDto>(authResult.Error);
        }

        var review = await _context.Reviews
            .Include(r => r.Product)
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Id == request.ReviewId, cancellationToken);

        if (review is null)
        {
            return Result.Failure<ManagedReviewDto>(Error.NotFound(
                "Review.NotFound",
                "Review was not found."));
        }

        var replyResult = review.SetStaffReply(request.Reply);
        if (replyResult.IsFailure)
        {
            return Result.Failure<ManagedReviewDto>(replyResult.Error);
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            _context.ClearTrackedChanges();
            return Result.Failure<ManagedReviewDto>(Error.Conflict(
                "Review.ConcurrencyConflict",
                "The review was modified concurrently."));
        }

        return Result.Success(new ManagedReviewDto(
            review.Id,
            review.Product.Id,
            review.Product.Name,
            review.Product.Sku,
            review.User.Id,
            review.User.FullName,
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

using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Features.Reviews.Common;
using RiuTek.Core.Common;

namespace RiuTek.Application.Features.Reviews.Commands;

public record DeleteReviewReplyCommand(Guid ReviewId) : IRequest<Result>;

public class DeleteReviewReplyCommandHandler : IRequestHandler<DeleteReviewReplyCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public DeleteReviewReplyCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result> Handle(
        DeleteReviewReplyCommand request,
        CancellationToken cancellationToken)
    {
        var authResult = await ReviewAuthHelper.ValidateActiveContentManagerAsync(
            _currentUserService,
            _context,
            cancellationToken);

        if (authResult.IsFailure)
        {
            return Result.Failure(authResult.Error);
        }

        var review = await _context.Reviews
            .FirstOrDefaultAsync(r => r.Id == request.ReviewId, cancellationToken);

        if (review is null)
        {
            return Result.Failure(Error.NotFound(
                "Review.NotFound",
                "Review was not found."));
        }

        review.ClearStaffReply();

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            _context.ClearTrackedChanges();
            return Result.Failure(Error.Conflict(
                "Review.ConcurrencyConflict",
                "The review was modified concurrently."));
        }

        return Result.Success();
    }
}

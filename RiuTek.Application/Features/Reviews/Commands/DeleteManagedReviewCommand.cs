using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Features.Reviews.Common;
using RiuTek.Core.Common;

namespace RiuTek.Application.Features.Reviews.Commands;

public record DeleteManagedReviewCommand(Guid ReviewId) : IRequest<Result>;

public class DeleteManagedReviewCommandHandler : IRequestHandler<DeleteManagedReviewCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public DeleteManagedReviewCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result> Handle(
        DeleteManagedReviewCommand request,
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

        _context.Reviews.Remove(review);

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

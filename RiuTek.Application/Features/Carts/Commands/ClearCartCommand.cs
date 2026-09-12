using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Features.Carts.Common;
using RiuTek.Core.Common;

namespace RiuTek.Application.Features.Carts.Commands;

public record ClearCartCommand : IRequest<Result>;

public class ClearCartCommandHandler : IRequestHandler<ClearCartCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public ClearCartCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result> Handle(
        ClearCartCommand request,
        CancellationToken cancellationToken)
    {
        var authResult = await CartAuthHelper.ValidateActiveUserAsync(_currentUserService, _context, cancellationToken);
        if (authResult.IsFailure)
        {
            return Result.Failure(authResult.Error);
        }

        var userId = authResult.Value;

        var cart = await _context.Carts
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);

        // Idempotent: if no cart row or cart is already empty, return success without saving
        if (cart is null || cart.Items.Count == 0)
        {
            return Result.Success();
        }

        cart.Clear();

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(Error.Conflict(
                "Cart.ConcurrencyConflict",
                "The cart was modified by another request. Please refresh and try again."));
        }

        return Result.Success();
    }
}

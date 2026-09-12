using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Features.Carts.Common;
using RiuTek.Core.Common;

namespace RiuTek.Application.Features.Carts.Commands;

public record RemoveCartItemCommand(Guid ProductId) : IRequest<Result>;

public class RemoveCartItemCommandValidator : AbstractValidator<RemoveCartItemCommand>
{
    public RemoveCartItemCommandValidator()
    {
        RuleFor(x => x.ProductId)
            .NotEmpty().WithMessage("ProductId must not be empty.");
    }
}

public class RemoveCartItemCommandHandler : IRequestHandler<RemoveCartItemCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public RemoveCartItemCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result> Handle(
        RemoveCartItemCommand request,
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

        if (cart is null)
        {
            return Result.Failure(Error.NotFound("Cart.ItemNotFound", "Item was not found in cart."));
        }

        var existingItem = cart.Items.FirstOrDefault(i => i.ProductId == request.ProductId);
        if (existingItem is null)
        {
            return Result.Failure(Error.NotFound("Cart.ItemNotFound", "Item was not found in cart."));
        }

        cart.RemoveItem(request.ProductId);

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

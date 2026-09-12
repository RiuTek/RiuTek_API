using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Core.Common;

namespace RiuTek.Application.Features.Carts.Common;

public static class CartAuthHelper
{
    public static async Task<Result<Guid>> ValidateActiveUserAsync(
        ICurrentUserService currentUserService,
        IApplicationDbContext context,
        CancellationToken cancellationToken)
    {
        if (!currentUserService.IsAuthenticated || currentUserService.UserId is null || currentUserService.UserId == Guid.Empty)
        {
            return Error.Unauthorized("Cart.Unauthorized", "Authentication is required to access the cart.");
        }

        var userId = currentUserService.UserId.Value;
        var user = await context.Users
            .AsNoTracking()
            .Select(u => new { u.Id, u.IsActive })
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
        {
            return Error.NotFound("Cart.UserNotFound", "User account was not found.");
        }

        if (!user.IsActive)
        {
            return Error.Forbidden("Cart.AccountInactive", "User account is inactive.");
        }

        return userId;
    }
}

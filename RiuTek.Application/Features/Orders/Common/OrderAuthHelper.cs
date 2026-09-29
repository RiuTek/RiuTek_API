using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;

namespace RiuTek.Application.Features.Orders.Common;

public static class OrderAuthHelper
{
    public static async Task<Result<User>> ValidateActiveUserAsync(
        ICurrentUserService currentUserService,
        IApplicationDbContext context,
        CancellationToken cancellationToken)
    {
        if (!currentUserService.IsAuthenticated || currentUserService.UserId is null || currentUserService.UserId == Guid.Empty)
        {
            return Result.Failure<User>(Error.Unauthorized("Checkout.Unauthorized", "Authentication is required for checkout."));
        }

        var userId = currentUserService.UserId.Value;
        var user = await context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
        {
            return Result.Failure<User>(Error.NotFound("Checkout.UserNotFound", "User account was not found."));
        }

        if (!user.IsActive)
        {
            return Result.Failure<User>(Error.Forbidden("Checkout.AccountInactive", "User account is inactive."));
        }

        return Result.Success(user);
    }
}

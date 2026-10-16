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

    public static async Task<Result<User>> ValidateActiveContentManagerAsync(
        ICurrentUserService currentUserService,
        IApplicationDbContext context,
        CancellationToken cancellationToken)
    {
        var userResult = await ValidateActiveUserAsync(
            currentUserService,
            context,
            cancellationToken);

        if (userResult.IsFailure)
        {
            return userResult;
        }

        if (!RiuTek.Application.Common.Authorization.AuthorizationRules.IsContentManager(
                userResult.Value.Role.ToString()))
        {
            return Result.Failure<User>(Error.Forbidden(
                "OrderManagement.Forbidden",
                "Content manager privileges are required."));
        }

        return userResult;
    }
}

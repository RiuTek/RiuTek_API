using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Authorization;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;

namespace RiuTek.Application.Features.Products.Common;

public static class StockAuthHelper
{
    public static async Task<Result<User>> ValidateActiveContentManagerAsync(
        ICurrentUserService currentUserService,
        IApplicationDbContext context,
        CancellationToken cancellationToken)
    {
        if (!currentUserService.IsAuthenticated || currentUserService.UserId is null || currentUserService.UserId == Guid.Empty)
        {
            return Result.Failure<User>(Error.Unauthorized("Auth.Unauthorized", "Authentication is required."));
        }

        if (!AuthorizationRules.IsContentManager(currentUserService.UserRole))
        {
            return Result.Failure<User>(Error.Forbidden("StockMovement.Forbidden", "Content manager privileges are required."));
        }

        var userId = currentUserService.UserId.Value;
        var user = await context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
        {
            return Result.Failure<User>(Error.NotFound("User.NotFound", "User account was not found."));
        }

        if (!user.IsActive)
        {
            return Result.Failure<User>(Error.Forbidden("User.AccountInactive", "User account is inactive."));
        }

        if (!AuthorizationRules.IsContentManager(user.Role.ToString()))
        {
            return Result.Failure<User>(Error.Forbidden("StockMovement.Forbidden", "Content manager privileges are required."));
        }

        return Result.Success(user);
    }
}

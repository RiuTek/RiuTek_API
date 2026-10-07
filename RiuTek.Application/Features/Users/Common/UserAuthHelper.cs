using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Core.Common;

namespace RiuTek.Application.Features.Users.Common;

public static class UserAuthHelper
{
    public static async Task<Result<Guid>> ValidateActiveUserAsync(
        ICurrentUserService currentUserService,
        IApplicationDbContext context,
        CancellationToken cancellationToken)
    {
        if (!currentUserService.IsAuthenticated || currentUserService.UserId is null || currentUserService.UserId == Guid.Empty)
        {
            return Result.Failure<Guid>(Error.Unauthorized("Auth.Unauthorized", "Authentication is required."));
        }

        var userId = currentUserService.UserId.Value;
        var user = await context.Users
            .AsNoTracking()
            .Select(u => new { u.Id, u.IsActive })
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
        {
            return Result.Failure<Guid>(Error.NotFound("User.NotFound", "User account was not found."));
        }

        if (!user.IsActive)
        {
            return Result.Failure<Guid>(Error.Forbidden("User.AccountInactive", "User account is inactive."));
        }

        return Result.Success(userId);
    }
}

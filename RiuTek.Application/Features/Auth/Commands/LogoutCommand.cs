using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Core.Common;

namespace RiuTek.Application.Features.Auth.Commands;

public record LogoutCommand(string? RefreshToken) : IRequest<Result>;

public class LogoutCommandHandler : IRequestHandler<LogoutCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly IRefreshTokenHasher _refreshTokenHasher;

    public LogoutCommandHandler(
        IApplicationDbContext context,
        IRefreshTokenHasher refreshTokenHasher)
    {
        _context = context;
        _refreshTokenHasher = refreshTokenHasher;
    }

    public async Task<Result> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return Result.Success();
        }

        var hashedToken = _refreshTokenHasher.HashToken(request.RefreshToken);

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.RefreshToken == hashedToken, cancellationToken);

        if (user != null)
        {
            user.RefreshToken = null;
            user.RefreshTokenExpiryTime = null;
            await _context.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
    }
}

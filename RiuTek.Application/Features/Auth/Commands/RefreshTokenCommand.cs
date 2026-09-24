using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Core.Common;

namespace RiuTek.Application.Features.Auth.Commands;

public record RefreshTokenCommand(
    string RefreshToken
) : IRequest<Result<AuthResponseDto>>;

public class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator()
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty().WithMessage("Refresh token không được để trống.");
    }
}

public class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, Result<AuthResponseDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly IRefreshTokenHasher _refreshTokenHasher;

    public RefreshTokenCommandHandler(
        IApplicationDbContext context,
        IJwtTokenGenerator jwtTokenGenerator,
        IRefreshTokenHasher refreshTokenHasher)
    {
        _context = context;
        _jwtTokenGenerator = jwtTokenGenerator;
        _refreshTokenHasher = refreshTokenHasher;
    }

    public async Task<Result<AuthResponseDto>> Handle(
        RefreshTokenCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Tìm User có Refresh Token tương ứng bằng hash
        var hashedToken = _refreshTokenHasher.HashToken(request.RefreshToken);

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.RefreshToken == hashedToken, cancellationToken);

        if (user == null)
        {
            return Result.Failure<AuthResponseDto>(Error.Unauthorized(
                "Auth.InvalidRefreshToken",
                "Refresh token không hợp lệ hoặc đã bị hủy."));
        }

        // 2. Kiểm tra Refresh Token còn hạn không
        if (user.RefreshTokenExpiryTime == null || user.RefreshTokenExpiryTime <= DateTime.UtcNow)
        {
            return Result.Failure<AuthResponseDto>(Error.Unauthorized(
                "Auth.RefreshTokenExpired",
                "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại."));
        }

        if (!user.IsActive)
        {
            return Result.Failure<AuthResponseDto>(Error.Forbidden(
                "Auth.AccountDisabled",
                "Tài khoản của bạn đã bị vô hiệu hóa."));
        }

        // 3. Cơ chế Token Rotation: Sinh Access Token mới + Refresh Token mới
        var refreshTokenExpiresAt = DateTime.UtcNow.AddDays(_jwtTokenGenerator.RefreshTokenExpiryDays);
        var newAccessToken = _jwtTokenGenerator.GenerateAccessToken(user);
        var newRefreshToken = _jwtTokenGenerator.GenerateRefreshToken();

        user.RefreshToken = _refreshTokenHasher.HashToken(newRefreshToken);
        user.RefreshTokenExpiryTime = refreshTokenExpiresAt;
        await _context.SaveChangesAsync(cancellationToken);

        var userDto = new UserDto(
            user.Id,
            user.Email,
            user.FullName,
            user.PhoneNumber,
            user.Role,
            user.CreatedAt
        );

        return Result.Success(new AuthResponseDto(
            AccessToken: newAccessToken,
            RefreshToken: newRefreshToken,
            ExpiresInSeconds: _jwtTokenGenerator.ExpiryInSeconds,
            User: userDto,
            RefreshTokenExpiresAt: refreshTokenExpiresAt
        ));
    }
}

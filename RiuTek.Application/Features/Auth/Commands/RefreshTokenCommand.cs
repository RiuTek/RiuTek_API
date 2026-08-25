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

    public RefreshTokenCommandHandler(
        IApplicationDbContext context,
        IJwtTokenGenerator jwtTokenGenerator)
    {
        _context = context;
        _jwtTokenGenerator = jwtTokenGenerator;
    }

    public async Task<Result<AuthResponseDto>> Handle(
        RefreshTokenCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Tìm User có Refresh Token tương ứng
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.RefreshToken == request.RefreshToken, cancellationToken);

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
        var newAccessToken = _jwtTokenGenerator.GenerateAccessToken(user);
        var newRefreshToken = _jwtTokenGenerator.GenerateRefreshToken();

        user.RefreshToken = newRefreshToken;
        user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7);
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
            ExpiresInSeconds: 3600,
            User: userDto
        ));
    }
}

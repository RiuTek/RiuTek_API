using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Common.Mappings;
using RiuTek.Application.DTOs;
using RiuTek.Core.Common;

namespace RiuTek.Application.Features.Users.Queries;

public record GetProfileQuery : IRequest<Result<UserProfileDto>>;

public class GetProfileQueryHandler : IRequestHandler<GetProfileQuery, Result<UserProfileDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public GetProfileQueryHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result<UserProfileDto>> Handle(
        GetProfileQuery request,
        CancellationToken cancellationToken)
    {
        if (!_currentUserService.IsAuthenticated || _currentUserService.UserId == null)
        {
            return Result.Failure<UserProfileDto>(Error.Unauthorized(
                "Auth.Unauthorized",
                "Bạn cần đăng nhập để xem thông tin cá nhân."));
        }

        var userId = _currentUserService.UserId.Value;

        var user = await _context.Users
            .AsNoTracking()
            .Include(u => u.Addresses)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user == null)
        {
            return Result.Failure<UserProfileDto>(Error.NotFound(
                "User.NotFound",
                "Không tìm thấy thông tin tài khoản."));
        }

        var profileDto = new UserProfileDto(
            user.Id,
            user.Email,
            user.FullName,
            user.PhoneNumber,
            user.Role,
            user.Addresses.Select(a => a.ToDto()).ToList(),
            user.CreatedAt
        );

        return Result.Success(profileDto);
    }
}

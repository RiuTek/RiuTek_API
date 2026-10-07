using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Core.Common;

namespace RiuTek.Application.Features.Users.Commands;

public record UpdateProfileCommand(
    string FullName,
    string? PhoneNumber
) : IRequest<Result<UserDto>>;

public class UpdateProfileCommandValidator : AbstractValidator<UpdateProfileCommand>
{
    public UpdateProfileCommandValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Họ và tên không được để trống.")
            .MaximumLength(150).WithMessage("Họ và tên không được vượt quá 150 ký tự.");

        RuleFor(x => x.PhoneNumber)
            .MaximumLength(20).WithMessage("Số điện thoại không được vượt quá 20 ký tự.")
            .When(x => !string.IsNullOrEmpty(x.PhoneNumber));
    }
}

public class UpdateProfileCommandHandler : IRequestHandler<UpdateProfileCommand, Result<UserDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public UpdateProfileCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result<UserDto>> Handle(
        UpdateProfileCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUserService.IsAuthenticated || _currentUserService.UserId == null || _currentUserService.UserId == Guid.Empty)
        {
            return Result.Failure<UserDto>(Error.Unauthorized(
                "Auth.Unauthorized",
                "Authentication is required."));
        }

        var userId = _currentUserService.UserId.Value;

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user == null)
        {
            return Result.Failure<UserDto>(Error.NotFound(
                "User.NotFound",
                "User account was not found."));
        }

        if (!user.IsActive)
        {
            return Result.Failure<UserDto>(Error.Forbidden(
                "User.AccountInactive",
                "User account is inactive."));
        }

        user.FullName = request.FullName.Trim();
        user.PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim();
        user.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(new UserDto(
            user.Id,
            user.Email,
            user.FullName,
            user.PhoneNumber,
            user.Role,
            user.CreatedAt
        ));
    }
}

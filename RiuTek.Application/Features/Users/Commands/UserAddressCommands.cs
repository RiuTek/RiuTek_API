using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Common.Mappings;
using RiuTek.Application.DTOs;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;

namespace RiuTek.Application.Features.Users.Commands;

// -------------------------------------------------------------
// 1. ADD USER ADDRESS
// -------------------------------------------------------------
public record AddUserAddressCommand(
    string ReceiverName,
    string PhoneNumber,
    string AddressLine,
    string Ward,
    string District,
    string City,
    bool IsDefault = false
) : IRequest<Result<UserAddressDto>>;

public class AddUserAddressCommandValidator : AbstractValidator<AddUserAddressCommand>
{
    public AddUserAddressCommandValidator()
    {
        RuleFor(x => x.ReceiverName)
            .NotEmpty().WithMessage("Tên người nhận không được để trống.")
            .MaximumLength(150).WithMessage("Tên người nhận không được vượt quá 150 ký tự.");

        RuleFor(x => x.PhoneNumber)
            .NotEmpty().WithMessage("Số điện thoại không được để trống.")
            .MaximumLength(20).WithMessage("Số điện thoại không được vượt quá 20 ký tự.");

        RuleFor(x => x.AddressLine)
            .NotEmpty().WithMessage("Địa chỉ chi tiết không được để trống.");

        RuleFor(x => x.City)
            .NotEmpty().WithMessage("Tỉnh/Thành phố không được để trống.");
    }
}

public class AddUserAddressCommandHandler : IRequestHandler<AddUserAddressCommand, Result<UserAddressDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public AddUserAddressCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result<UserAddressDto>> Handle(
        AddUserAddressCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUserService.IsAuthenticated || _currentUserService.UserId == null)
        {
            return Result.Failure<UserAddressDto>(Error.Unauthorized(
                "Auth.Unauthorized",
                "Bạn cần đăng nhập để thêm địa chỉ."));
        }

        var userId = _currentUserService.UserId.Value;

        // Nếu đặt làm địa chỉ mặc định, reset các địa chỉ cũ của User về false
        if (request.IsDefault)
        {
            var existingAddresses = await _context.UserAddresses
                .Where(a => a.UserId == userId && a.IsDefault)
                .ToListAsync(cancellationToken);

            foreach (var addr in existingAddresses)
            {
                addr.IsDefault = false;
            }
        }
        else
        {
            // Nếu đây là địa chỉ đầu tiên của User, tự động đặt làm mặc định
            var hasAnyAddress = await _context.UserAddresses
                .AnyAsync(a => a.UserId == userId, cancellationToken);

            if (!hasAnyAddress)
            {
                // First address becomes default automatically
            }
        }

        var newAddress = new UserAddress(
            userId: userId,
            receiverName: request.ReceiverName.Trim(),
            phoneNumber: request.PhoneNumber.Trim(),
            addressLine: request.AddressLine.Trim(),
            ward: request.Ward?.Trim() ?? string.Empty,
            district: request.District?.Trim() ?? string.Empty,
            city: request.City.Trim(),
            isDefault: request.IsDefault
        );

        _context.UserAddresses.Add(newAddress);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(newAddress.ToDto());
    }
}

// -------------------------------------------------------------
// 2. DELETE USER ADDRESS
// -------------------------------------------------------------
public record DeleteUserAddressCommand(Guid AddressId) : IRequest<Result>;

public class DeleteUserAddressCommandHandler : IRequestHandler<DeleteUserAddressCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public DeleteUserAddressCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result> Handle(
        DeleteUserAddressCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUserService.IsAuthenticated || _currentUserService.UserId == null)
        {
            return Result.Failure(Error.Unauthorized(
                "Auth.Unauthorized",
                "Bạn cần đăng nhập để xóa địa chỉ."));
        }

        var userId = _currentUserService.UserId.Value;

        var address = await _context.UserAddresses
            .FirstOrDefaultAsync(a => a.Id == request.AddressId && a.UserId == userId, cancellationToken);

        if (address == null)
        {
            return Result.Failure(Error.NotFound(
                "UserAddress.NotFound",
                "Không tìm thấy địa chỉ hoặc bạn không có quyền xóa địa chỉ này."));
        }

        _context.UserAddresses.Remove(address);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

// -------------------------------------------------------------
// 3. SET DEFAULT ADDRESS
// -------------------------------------------------------------
public record SetDefaultAddressCommand(Guid AddressId) : IRequest<Result>;

public class SetDefaultAddressCommandHandler : IRequestHandler<SetDefaultAddressCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public SetDefaultAddressCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result> Handle(
        SetDefaultAddressCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUserService.IsAuthenticated || _currentUserService.UserId == null)
        {
            return Result.Failure(Error.Unauthorized(
                "Auth.Unauthorized",
                "Bạn cần đăng nhập để thực hiện thao tác này."));
        }

        var userId = _currentUserService.UserId.Value;

        var addresses = await _context.UserAddresses
            .Where(a => a.UserId == userId)
            .ToListAsync(cancellationToken);

        var targetAddress = addresses.FirstOrDefault(a => a.Id == request.AddressId);
        if (targetAddress == null)
        {
            return Result.Failure(Error.NotFound(
                "UserAddress.NotFound",
                "Không tìm thấy địa chỉ cần đặt mặc định."));
        }

        foreach (var addr in addresses)
        {
            addr.IsDefault = addr.Id == request.AddressId;
        }

        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

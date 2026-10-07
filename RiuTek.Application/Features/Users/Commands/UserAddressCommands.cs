using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Common.Mappings;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Users.Common;
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
            .NotEmpty().WithMessage("Địa chỉ chi tiết không được để trống.")
            .MaximumLength(300).WithMessage("Địa chỉ chi tiết không được vượt quá 300 ký tự.");

        RuleFor(x => x.Ward)
            .NotEmpty().WithMessage("Phường/Xã không được để trống.")
            .MaximumLength(100).WithMessage("Phường/Xã không được vượt quá 100 ký tự.");

        RuleFor(x => x.District)
            .NotEmpty().WithMessage("Quận/Huyện không được để trống.")
            .MaximumLength(100).WithMessage("Quận/Huyện không được vượt quá 100 ký tự.");

        RuleFor(x => x.City)
            .NotEmpty().WithMessage("Tỉnh/Thành phố không được để trống.")
            .MaximumLength(100).WithMessage("Tỉnh/Thành phố không được vượt quá 100 ký tự.");
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
        var authResult = await UserAuthHelper.ValidateActiveUserAsync(
            _currentUserService,
            _context,
            cancellationToken);

        if (!authResult.IsSuccess)
        {
            return Result.Failure<UserAddressDto>(authResult.Error);
        }

        var userId = authResult.Value;

        var hasAnyAddress = await _context.UserAddresses
            .AnyAsync(a => a.UserId == userId, cancellationToken);

        // First address always becomes default. Otherwise, follow request.IsDefault.
        var isDefault = !hasAnyAddress || request.IsDefault;

        if (isDefault && hasAnyAddress)
        {
            var existingDefaultAddresses = await _context.UserAddresses
                .Where(a => a.UserId == userId && a.IsDefault)
                .ToListAsync(cancellationToken);

            var now = DateTime.UtcNow;
            foreach (var addr in existingDefaultAddresses)
            {
                addr.IsDefault = false;
                addr.UpdatedAt = now;
            }
        }

        var newAddress = new UserAddress(
            userId: userId,
            receiverName: request.ReceiverName.Trim(),
            phoneNumber: request.PhoneNumber.Trim(),
            addressLine: request.AddressLine.Trim(),
            ward: request.Ward.Trim(),
            district: request.District.Trim(),
            city: request.City.Trim(),
            isDefault: isDefault
        );

        _context.UserAddresses.Add(newAddress);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(newAddress.ToDto());
    }
}

// -------------------------------------------------------------
// 2. UPDATE USER ADDRESS
// -------------------------------------------------------------
public record UpdateUserAddressCommand(
    Guid AddressId,
    string ReceiverName,
    string PhoneNumber,
    string AddressLine,
    string Ward,
    string District,
    string City
) : IRequest<Result<UserAddressDto>>;

public class UpdateUserAddressCommandValidator : AbstractValidator<UpdateUserAddressCommand>
{
    public UpdateUserAddressCommandValidator()
    {
        RuleFor(x => x.AddressId)
            .NotEmpty().WithMessage("Mã địa chỉ không được để trống.");

        RuleFor(x => x.ReceiverName)
            .NotEmpty().WithMessage("Tên người nhận không được để trống.")
            .MaximumLength(150).WithMessage("Tên người nhận không được vượt quá 150 ký tự.");

        RuleFor(x => x.PhoneNumber)
            .NotEmpty().WithMessage("Số điện thoại không được để trống.")
            .MaximumLength(20).WithMessage("Số điện thoại không được vượt quá 20 ký tự.");

        RuleFor(x => x.AddressLine)
            .NotEmpty().WithMessage("Địa chỉ chi tiết không được để trống.")
            .MaximumLength(300).WithMessage("Địa chỉ chi tiết không được vượt quá 300 ký tự.");

        RuleFor(x => x.Ward)
            .NotEmpty().WithMessage("Phường/Xã không được để trống.")
            .MaximumLength(100).WithMessage("Phường/Xã không được vượt quá 100 ký tự.");

        RuleFor(x => x.District)
            .NotEmpty().WithMessage("Quận/Huyện không được để trống.")
            .MaximumLength(100).WithMessage("Quận/Huyện không được vượt quá 100 ký tự.");

        RuleFor(x => x.City)
            .NotEmpty().WithMessage("Tỉnh/Thành phố không được để trống.")
            .MaximumLength(100).WithMessage("Tỉnh/Thành phố không được vượt quá 100 ký tự.");
    }
}

public class UpdateUserAddressCommandHandler : IRequestHandler<UpdateUserAddressCommand, Result<UserAddressDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public UpdateUserAddressCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result<UserAddressDto>> Handle(
        UpdateUserAddressCommand request,
        CancellationToken cancellationToken)
    {
        var authResult = await UserAuthHelper.ValidateActiveUserAsync(
            _currentUserService,
            _context,
            cancellationToken);

        if (!authResult.IsSuccess)
        {
            return Result.Failure<UserAddressDto>(authResult.Error);
        }

        var userId = authResult.Value;

        var address = await _context.UserAddresses
            .FirstOrDefaultAsync(a => a.Id == request.AddressId && a.UserId == userId, cancellationToken);

        if (address == null)
        {
            return Result.Failure<UserAddressDto>(Error.NotFound(
                "UserAddress.NotFound",
                "Không tìm thấy địa chỉ hoặc bạn không có quyền truy cập địa chỉ này."));
        }

        address.ReceiverName = request.ReceiverName.Trim();
        address.PhoneNumber = request.PhoneNumber.Trim();
        address.AddressLine = request.AddressLine.Trim();
        address.Ward = request.Ward.Trim();
        address.District = request.District.Trim();
        address.City = request.City.Trim();
        address.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(address.ToDto());
    }
}

// -------------------------------------------------------------
// 3. SET DEFAULT ADDRESS
// -------------------------------------------------------------
public record SetDefaultAddressCommand(Guid AddressId) : IRequest<Result>;

public class SetDefaultAddressCommandValidator : AbstractValidator<SetDefaultAddressCommand>
{
    public SetDefaultAddressCommandValidator()
    {
        RuleFor(x => x.AddressId)
            .NotEmpty().WithMessage("Mã địa chỉ không được để trống.");
    }
}

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
        var authResult = await UserAuthHelper.ValidateActiveUserAsync(
            _currentUserService,
            _context,
            cancellationToken);

        if (!authResult.IsSuccess)
        {
            return Result.Failure(authResult.Error);
        }

        var userId = authResult.Value;

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

        if (targetAddress.IsDefault)
        {
            // Idempotent: already default
            return Result.Success();
        }

        var now = DateTime.UtcNow;
        foreach (var addr in addresses)
        {
            if (addr.Id == request.AddressId)
            {
                addr.IsDefault = true;
                addr.UpdatedAt = now;
            }
            else if (addr.IsDefault)
            {
                addr.IsDefault = false;
                addr.UpdatedAt = now;
            }
        }

        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

// -------------------------------------------------------------
// 4. DELETE USER ADDRESS
// -------------------------------------------------------------
public record DeleteUserAddressCommand(Guid AddressId) : IRequest<Result>;

public class DeleteUserAddressCommandValidator : AbstractValidator<DeleteUserAddressCommand>
{
    public DeleteUserAddressCommandValidator()
    {
        RuleFor(x => x.AddressId)
            .NotEmpty().WithMessage("Mã địa chỉ không được để trống.");
    }
}

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
        var authResult = await UserAuthHelper.ValidateActiveUserAsync(
            _currentUserService,
            _context,
            cancellationToken);

        if (!authResult.IsSuccess)
        {
            return Result.Failure(authResult.Error);
        }

        var userId = authResult.Value;

        var addresses = await _context.UserAddresses
            .Where(a => a.UserId == userId)
            .ToListAsync(cancellationToken);

        var target = addresses.FirstOrDefault(a => a.Id == request.AddressId);
        if (target == null)
        {
            return Result.Failure(Error.NotFound(
                "UserAddress.NotFound",
                "Không tìm thấy địa chỉ hoặc bạn không có quyền xóa địa chỉ này."));
        }

        var wasDefault = target.IsDefault;
        _context.UserAddresses.Remove(target);

        var remaining = addresses.Where(a => a.Id != target.Id).ToList();
        if (wasDefault && remaining.Count > 0)
        {
            var nextDefault = remaining
                .OrderBy(a => a.CreatedAt)
                .ThenBy(a => a.Id)
                .First();

            nextDefault.IsDefault = true;
            nextDefault.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

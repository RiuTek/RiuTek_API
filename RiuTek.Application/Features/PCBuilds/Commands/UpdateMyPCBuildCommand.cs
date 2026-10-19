using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.PCBuilds.Common;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;

namespace RiuTek.Application.Features.PCBuilds.Commands;

public record UpdateMyPCBuildCommand(
    Guid Id,
    string Name,
    string? Description,
    List<PCBuildItemInputDto> Items
) : IRequest<Result<MyPCBuildDto>>;

public class UpdateMyPCBuildCommandValidator : AbstractValidator<UpdateMyPCBuildCommand>
{
    public UpdateMyPCBuildCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Id cấu hình PC không hợp lệ.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Tên cấu hình PC không được để trống.")
            .MaximumLength(200).WithMessage("Tên cấu hình PC không được vượt quá 200 ký tự.");

        RuleFor(x => x.Description)
            .MaximumLength(1000).WithMessage("Mô tả cấu hình PC không được vượt quá 1000 ký tự.")
            .When(x => !string.IsNullOrEmpty(x.Description));

        RuleFor(x => x.Items)
            .NotEmpty().WithMessage("Cấu hình PC phải có ít nhất 1 linh kiện.")
            .Must(x => x != null && x.Count <= 30).WithMessage("Cấu hình PC chỉ được chứa tối đa 30 linh kiện.");

        RuleForEach(x => x.Items).SetValidator(new PCBuildItemInputValidator());
    }
}

public class UpdateMyPCBuildCommandHandler : IRequestHandler<UpdateMyPCBuildCommand, Result<MyPCBuildDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly IHardwareCompatibilityService _compatibilityService;

    public UpdateMyPCBuildCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        IHardwareCompatibilityService compatibilityService)
    {
        _context = context;
        _currentUserService = currentUserService;
        _compatibilityService = compatibilityService;
    }

    public async Task<Result<MyPCBuildDto>> Handle(
        UpdateMyPCBuildCommand request,
        CancellationToken cancellationToken)
    {
        var authResult = await PCBuildAuthHelper.ValidateActiveCustomerAsync(
            _currentUserService,
            _context,
            cancellationToken);

        if (authResult.IsFailure)
        {
            return Result.Failure<MyPCBuildDto>(authResult.Error);
        }

        var customer = authResult.Value;

        var pcBuild = await _context.PCBuilds
            .Include(b => b.Items)
            .FirstOrDefaultAsync(b => b.Id == request.Id && b.UserId == customer.Id, cancellationToken);

        if (pcBuild is null)
        {
            return Result.Failure<MyPCBuildDto>(Error.NotFound(
                "PCBuild.NotFound",
                $"Không tìm thấy cấu hình PC với Id '{request.Id}'."));
        }

        var resolveResult = await PCBuildValidationHelper.ValidateAndResolveItemsAsync(
            request.Items,
            _context,
            cancellationToken);

        if (resolveResult.IsFailure)
        {
            return Result.Failure<MyPCBuildDto>(resolveResult.Error);
        }

        var resolvedItems = resolveResult.Value;

        var selections = resolvedItems
            .Select(r => new ComponentSelection(r.Product, r.Quantity))
            .ToList();

        var compatibility = _compatibilityService.ValidateComponentSelections(selections);

        pcBuild.UpdateDetails(request.Name, request.Description);

        var productDict = resolvedItems.ToDictionary(r => r.Product.Id, r => r.Product);

        _context.PCBuildItems.RemoveRange(pcBuild.Items);

        var newItems = resolvedItems.Select(item => new PCBuildItem(
            pcBuildId: pcBuild.Id,
            productId: item.Product.Id,
            componentType: item.Product.ComponentType,
            unitPrice: item.Product.Price,
            quantity: item.Quantity
        )).ToList();

        pcBuild.ReplaceItems(newItems);
        _context.PCBuildItems.AddRange(newItems);

        pcBuild.ApplyCompatibilitySnapshot(
            compatibility.IsCompatible,
            compatibility.EstimatedWattage,
            compatibility.Issues.Select(i => $"[{i.Severity}] {i.Message}"));

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            _context.ClearTrackedChanges();
            return Result.Failure<MyPCBuildDto>(Error.Conflict(
                "PCBuild.ConcurrencyConflict",
                "Cấu hình PC đã bị thay đổi bởi thao tác khác. Vui lòng tải lại và thử lại."));
        }

        var resultDto = PCBuildResponseBuilder.BuildMyBuildDto(pcBuild, compatibility, productDict);
        return Result.Success(resultDto);
    }
}

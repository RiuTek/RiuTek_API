using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Common.Mappings;
using RiuTek.Application.DTOs;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Features.PCBuilds.Commands;

public record PCBuildItemRequest(Guid ProductId, int Quantity = 1);

public record CreatePCBuildCommand(
    string Name,
    string? Description,
    Guid? UserId,
    List<PCBuildItemRequest> Items
) : IRequest<Result<PCBuildDto>>;

public class CreatePCBuildCommandValidator : AbstractValidator<CreatePCBuildCommand>
{
    public CreatePCBuildCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Tên cấu hình PC không được để trống.")
            .MaximumLength(200).WithMessage("Tên cấu hình PC không được vượt quá 200 ký tự.");

        RuleFor(x => x.Items)
            .NotEmpty().WithMessage("Cấu hình PC phải có ít nhất 1 linh kiện.");

        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId).NotEmpty().WithMessage("Id sản phẩm không hợp lệ.");
            item.RuleFor(i => i.Quantity).GreaterThan(0).WithMessage("Số lượng linh kiện phải lớn hơn 0.");
        });
    }
}

public class CreatePCBuildCommandHandler : IRequestHandler<CreatePCBuildCommand, Result<PCBuildDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IHardwareCompatibilityService _compatibilityService;

    public CreatePCBuildCommandHandler(
        IApplicationDbContext context,
        IHardwareCompatibilityService compatibilityService)
    {
        _context = context;
        _compatibilityService = compatibilityService;
    }

    public async Task<Result<PCBuildDto>> Handle(
        CreatePCBuildCommand request,
        CancellationToken cancellationToken)
    {
        var productIds = request.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await _context.Products
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        if (products.Count != productIds.Count)
        {
            return Result.Failure<PCBuildDto>(Error.NotFound(
                "PCBuild.ProductNotFound",
                "Một hoặc nhiều linh kiện không tồn tại trong hệ thống."));
        }

        // Validate compatibility and power consumption
        var compatibility = _compatibilityService.ValidateComponents(products.Values.ToList());

        // Create PCBuild aggregate
        var pcBuild = new PCBuild(
            name: request.Name,
            userId: request.UserId,
            description: request.Description
        )
        {
            IsCompatible = compatibility.IsCompatible,
            EstimatedWattage = compatibility.EstimatedWattage,
            CompatibilityNotes = compatibility.Issues.Select(i => $"[{i.Severity}] {i.Message}").ToList(),
            Status = PCBuildStatus.Saved
        };

        // Add PCBuild items with snapshot price
        foreach (var itemReq in request.Items)
        {
            var product = products[itemReq.ProductId];
            var buildItem = new PCBuildItem(
                pcBuildId: pcBuild.Id,
                productId: product.Id,
                componentType: product.ComponentType,
                unitPrice: product.Price,
                quantity: itemReq.Quantity
            )
            {
                Product = product
            };
            pcBuild.Items.Add(buildItem);
        }

        pcBuild.RecalculateTotals();

        _context.PCBuilds.Add(pcBuild);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(pcBuild.ToDto());
    }
}

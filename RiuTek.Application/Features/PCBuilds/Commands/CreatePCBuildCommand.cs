using FluentValidation;
using MediatR;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.PCBuilds.Common;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;

namespace RiuTek.Application.Features.PCBuilds.Commands;

public record CreatePCBuildCommand(
    string Name,
    string? Description,
    List<PCBuildItemInputDto> Items
) : IRequest<Result<MyPCBuildDto>>;

public class CreatePCBuildCommandValidator : AbstractValidator<CreatePCBuildCommand>
{
    public CreatePCBuildCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Tên cấu hình PC không được để trống.")
            .MaximumLength(200).WithMessage("Tên cấu hình PC không được vượt quá 200 ký tự.");

        RuleFor(x => x.Description)
            .MaximumLength(1000).WithMessage("Mô tả cấu hình PC không được vượt quá 1000 ký tự.")
            .When(x => !string.IsNullOrEmpty(x.Description));

        RuleFor(x => x.Items)
            .NotEmpty().WithMessage("Cấu hình PC phải có ít nhất 1 linh kiện.")
            .Must(x => x != null && x.Count <= 30).WithMessage("Cấu hình PC chỉ được chứa tối đa 30 linh kiện.");

        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId).NotEmpty().WithMessage("Id sản phẩm không hợp lệ.");
            item.RuleFor(i => i.Quantity).InclusiveBetween(1, 20).WithMessage("Số lượng linh kiện phải từ 1 đến 20.");
        });
    }
}

public class CreatePCBuildCommandHandler : IRequestHandler<CreatePCBuildCommand, Result<MyPCBuildDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly IHardwareCompatibilityService _compatibilityService;

    public CreatePCBuildCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        IHardwareCompatibilityService compatibilityService)
    {
        _context = context;
        _currentUserService = currentUserService;
        _compatibilityService = compatibilityService;
    }

    public async Task<Result<MyPCBuildDto>> Handle(
        CreatePCBuildCommand request,
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

        var pcBuild = new PCBuild(
            name: request.Name,
            userId: customer.Id,
            description: request.Description,
            isAiGenerated: false,
            aiRationale: null
        );

        var productDict = resolvedItems.ToDictionary(r => r.Product.Id, r => r.Product);

        foreach (var item in resolvedItems)
        {
            var buildItem = new PCBuildItem(
                pcBuildId: pcBuild.Id,
                productId: item.Product.Id,
                componentType: item.Product.ComponentType,
                unitPrice: item.Product.Price,
                quantity: item.Quantity
            );

            pcBuild.AddItem(buildItem);
        }

        pcBuild.ApplyCompatibilitySnapshot(
            compatibility.IsCompatible,
            compatibility.EstimatedWattage,
            compatibility.Issues.Select(i => $"[{i.Severity}] {i.Message}"));

        _context.PCBuilds.Add(pcBuild);
        await _context.SaveChangesAsync(cancellationToken);

        var myItems = pcBuild.Items.Select(i =>
        {
            var product = productDict[i.ProductId];
            var canPurchase = product.IsActive && product.StockQuantity >= i.Quantity;
            return new MyPCBuildItemDto(
                Id: i.Id,
                ProductId: i.ProductId,
                ProductName: product.Name,
                ProductSlug: product.Slug,
                ProductSku: product.Sku,
                ProductImageUrl: product.ImageUrl,
                ComponentType: i.ComponentType,
                Quantity: i.Quantity,
                UnitPrice: i.UnitPrice,
                LineTotal: i.UnitPrice * i.Quantity,
                CurrentPrice: product.Price,
                StockQuantity: product.StockQuantity,
                IsActive: product.IsActive,
                CanPurchase: canPurchase,
                Specifications: product.Specifications
            );
        }).ToList();

        var canPurchaseAll = myItems.Count > 0 && myItems.All(i => i.CanPurchase);

        return Result.Success(new MyPCBuildDto(
            Id: pcBuild.Id,
            Name: pcBuild.Name,
            Description: pcBuild.Description,
            TotalPrice: pcBuild.TotalPrice,
            EstimatedWattage: pcBuild.EstimatedWattage,
            RecommendedPsuWattage: compatibility.RecommendedPsuWattage,
            IsCompatible: pcBuild.IsCompatible,
            IsCompleteSystem: compatibility.IsCompleteSystem,
            MissingComponents: compatibility.MissingComponents,
            Issues: compatibility.Issues,
            CanPurchaseAll: canPurchaseAll,
            Status: pcBuild.Status,
            Items: myItems,
            CreatedAt: pcBuild.CreatedAt,
            UpdatedAt: pcBuild.UpdatedAt
        ));
    }
}

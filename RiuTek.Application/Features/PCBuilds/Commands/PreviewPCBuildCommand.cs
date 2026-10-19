using FluentValidation;
using MediatR;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.PCBuilds.Common;
using RiuTek.Core.Common;

namespace RiuTek.Application.Features.PCBuilds.Commands;

public record PreviewPCBuildCommand(
    List<PCBuildItemInputDto> Items
) : IRequest<Result<PCBuildPreviewResultDto>>;

public class PreviewPCBuildCommandValidator : AbstractValidator<PreviewPCBuildCommand>
{
    public PreviewPCBuildCommandValidator()
    {
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

public class PreviewPCBuildCommandHandler : IRequestHandler<PreviewPCBuildCommand, Result<PCBuildPreviewResultDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IHardwareCompatibilityService _compatibilityService;

    public PreviewPCBuildCommandHandler(
        IApplicationDbContext context,
        IHardwareCompatibilityService compatibilityService)
    {
        _context = context;
        _compatibilityService = compatibilityService;
    }

    public async Task<Result<PCBuildPreviewResultDto>> Handle(
        PreviewPCBuildCommand request,
        CancellationToken cancellationToken)
    {
        var resolveResult = await PCBuildValidationHelper.ValidateAndResolveItemsAsync(
            request.Items,
            _context,
            cancellationToken);

        if (resolveResult.IsFailure)
        {
            return Result.Failure<PCBuildPreviewResultDto>(resolveResult.Error);
        }

        var resolvedItems = resolveResult.Value;

        var selections = resolvedItems
            .Select(r => new ComponentSelection(r.Product, r.Quantity))
            .ToList();

        var compatibility = _compatibilityService.ValidateComponentSelections(selections);

        decimal totalPrice;
        checked
        {
            totalPrice = resolvedItems.Sum(r => r.Product.Price * r.Quantity);
        }

        var previewItems = resolvedItems.Select(r =>
        {
            var canPurchase = r.Product.IsActive && r.Product.StockQuantity >= r.Quantity;
            return new PCBuildPreviewItemDto(
                ProductId: r.Product.Id,
                Name: r.Product.Name,
                Slug: r.Product.Slug,
                Sku: r.Product.Sku,
                ImageUrl: r.Product.ImageUrl,
                ComponentType: r.Product.ComponentType,
                Quantity: r.Quantity,
                CurrentUnitPrice: r.Product.Price,
                LineTotal: r.Product.Price * r.Quantity,
                StockQuantity: r.Product.StockQuantity,
                IsActive: r.Product.IsActive,
                CanPurchase: canPurchase,
                IssueCode: null,
                Specifications: r.Product.Specifications
            );
        }).ToList();

        var canPurchaseAll = previewItems.Count > 0 && previewItems.All(i => i.CanPurchase);

        return Result.Success(new PCBuildPreviewResultDto(
            TotalPrice: totalPrice,
            EstimatedWattage: compatibility.EstimatedWattage,
            RecommendedPsuWattage: compatibility.RecommendedPsuWattage,
            IsCompatible: compatibility.IsCompatible,
            IsCompleteSystem: compatibility.IsCompleteSystem,
            MissingComponents: compatibility.MissingComponents,
            Issues: compatibility.Issues,
            CanPurchaseAll: canPurchaseAll,
            Items: previewItems
        ));
    }
}

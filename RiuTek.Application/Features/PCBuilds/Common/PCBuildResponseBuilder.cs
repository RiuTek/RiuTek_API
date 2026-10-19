using RiuTek.Application.DTOs;
using RiuTek.Core.Entities;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Features.PCBuilds.Common;

public static class PCBuildResponseBuilder
{
    public const string IssueProductInactive = "PRODUCT_INACTIVE";
    public const string IssueInsufficientStock = "INSUFFICIENT_STOCK";

    public static string? DetermineIssueCode(bool isActive, int stockQuantity, int requestedQuantity)
    {
        if (!isActive)
        {
            return IssueProductInactive;
        }

        if (stockQuantity < requestedQuantity)
        {
            return IssueInsufficientStock;
        }

        return null;
    }

    public static bool IsCanPurchase(string? issueCode) => issueCode is null;

    public static PCBuildPreviewItemDto BuildPreviewItem(Product product, int quantity)
    {
        var issueCode = DetermineIssueCode(product.IsActive, product.StockQuantity, quantity);
        var canPurchase = IsCanPurchase(issueCode);

        return new PCBuildPreviewItemDto(
            ProductId: product.Id,
            Name: product.Name,
            Slug: product.Slug,
            Sku: product.Sku,
            ImageUrl: product.ImageUrl,
            ComponentType: product.ComponentType,
            Quantity: quantity,
            CurrentUnitPrice: product.Price,
            LineTotal: product.Price * quantity,
            StockQuantity: product.StockQuantity,
            IsActive: product.IsActive,
            CanPurchase: canPurchase,
            IssueCode: issueCode,
            Specifications: product.Specifications
        );
    }

    public static PCBuildPreviewResultDto BuildPreviewResult(
        IEnumerable<PCBuildItemResolved> resolvedItems,
        CompatibilityCheckResultDto compatibility)
    {
        var previewItems = resolvedItems
            .OrderBy(r => r.Product.ComponentType)
            .ThenBy(r => r.Product.Id)
            .Select(r => BuildPreviewItem(r.Product, r.Quantity))
            .ToList();

        decimal totalPrice;
        checked
        {
            totalPrice = previewItems.Sum(i => i.LineTotal);
        }

        var canPurchaseAll = previewItems.Count > 0 && previewItems.All(i => i.CanPurchase);

        return new PCBuildPreviewResultDto(
            TotalPrice: totalPrice,
            EstimatedWattage: compatibility.EstimatedWattage,
            RecommendedPsuWattage: compatibility.RecommendedPsuWattage,
            IsCompatible: compatibility.IsCompatible,
            IsCompleteSystem: compatibility.IsCompleteSystem,
            MissingComponents: compatibility.MissingComponents,
            Issues: compatibility.Issues,
            CanPurchaseAll: canPurchaseAll,
            Items: previewItems
        );
    }

    public static MyPCBuildItemDto BuildMyBuildItem(
        Guid itemId,
        Guid productId,
        ComponentType componentType,
        int quantity,
        decimal unitPrice,
        Product? product)
    {
        var isActive = product?.IsActive ?? false;
        var stock = product?.StockQuantity ?? 0;
        var currentPrice = product?.Price ?? unitPrice;
        var issueCode = DetermineIssueCode(isActive, stock, quantity);
        var canPurchase = IsCanPurchase(issueCode);

        return new MyPCBuildItemDto(
            Id: itemId,
            ProductId: productId,
            ProductName: product?.Name ?? string.Empty,
            ProductSlug: product?.Slug ?? string.Empty,
            ProductSku: product?.Sku ?? string.Empty,
            ProductImageUrl: product?.ImageUrl ?? string.Empty,
            ComponentType: componentType,
            Quantity: quantity,
            UnitPrice: unitPrice,
            LineTotal: unitPrice * quantity,
            CurrentPrice: currentPrice,
            StockQuantity: stock,
            IsActive: isActive,
            CanPurchase: canPurchase,
            Specifications: product?.Specifications
        );
    }

    public static MyPCBuildDto BuildMyBuildDto(
        PCBuild pcBuild,
        CompatibilityCheckResultDto compatibility,
        IReadOnlyDictionary<Guid, Product>? productLookup = null)
    {
        var myItems = pcBuild.Items
            .OrderBy(i => i.ComponentType)
            .ThenBy(i => i.ProductId)
            .Select(i =>
            {
                Product? product = null;
                if (productLookup != null && productLookup.TryGetValue(i.ProductId, out var p))
                {
                    product = p;
                }
                else
                {
                    product = i.Product;
                }

                return BuildMyBuildItem(
                    itemId: i.Id,
                    productId: i.ProductId,
                    componentType: i.ComponentType,
                    quantity: i.Quantity,
                    unitPrice: i.UnitPrice,
                    product: product);
            })
            .ToList();

        var canPurchaseAll = myItems.Count > 0 && myItems.All(i => i.CanPurchase);

        return new MyPCBuildDto(
            Id: pcBuild.Id,
            Name: pcBuild.Name,
            Description: pcBuild.Description,
            TotalPrice: pcBuild.TotalPrice,
            EstimatedWattage: compatibility.EstimatedWattage,
            RecommendedPsuWattage: compatibility.RecommendedPsuWattage,
            IsCompatible: compatibility.IsCompatible,
            IsCompleteSystem: compatibility.IsCompleteSystem,
            MissingComponents: compatibility.MissingComponents,
            Issues: compatibility.Issues,
            CanPurchaseAll: canPurchaseAll,
            Status: pcBuild.Status,
            Items: myItems,
            CreatedAt: pcBuild.CreatedAt,
            UpdatedAt: pcBuild.UpdatedAt
        );
    }

    public static MyPCBuildSummaryDto BuildMyBuildSummaryDto(
        PCBuild pcBuild,
        CompatibilityCheckResultDto compatibility)
    {
        var canPurchaseAll = pcBuild.Items.Count > 0 && pcBuild.Items.All(i =>
        {
            var product = i.Product;
            var isActive = product?.IsActive ?? false;
            var stock = product?.StockQuantity ?? 0;
            var issueCode = DetermineIssueCode(isActive, stock, i.Quantity);
            return IsCanPurchase(issueCode);
        });

        return new MyPCBuildSummaryDto(
            Id: pcBuild.Id,
            Name: pcBuild.Name,
            Description: pcBuild.Description,
            TotalPrice: pcBuild.TotalPrice,
            EstimatedWattage: compatibility.EstimatedWattage,
            IsCompatible: compatibility.IsCompatible,
            CanPurchaseAll: canPurchaseAll,
            ItemCount: pcBuild.Items.Count,
            Status: pcBuild.Status,
            CreatedAt: pcBuild.CreatedAt,
            UpdatedAt: pcBuild.UpdatedAt
        );
    }
}

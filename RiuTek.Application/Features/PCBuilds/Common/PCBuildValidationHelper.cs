using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Features.PCBuilds.Common;

public record PCBuildItemResolved(Product Product, int Quantity);

public static class PCBuildValidationHelper
{
    private static readonly HashSet<ComponentType> SingleChoiceTypes =
    [
        ComponentType.Cpu,
        ComponentType.Motherboard,
        ComponentType.Gpu,
        ComponentType.Psu,
        ComponentType.Case,
        ComponentType.Cooler
    ];

    public static async Task<Result<List<PCBuildItemResolved>>> ValidateAndResolveItemsAsync(
        IReadOnlyList<PCBuildItemInputDto> itemInputs,
        IApplicationDbContext context,
        CancellationToken cancellationToken)
    {
        if (itemInputs == null || itemInputs.Count == 0)
        {
            return Result.Failure<List<PCBuildItemResolved>>(Error.Validation(
                "PCBuild.EmptyItems",
                "Cấu hình PC phải có ít nhất 1 linh kiện."));
        }

        if (itemInputs.Count > 30)
        {
            return Result.Failure<List<PCBuildItemResolved>>(Error.Validation(
                "PCBuild.TooManyItems",
                "Cấu hình PC chỉ được chứa tối đa 30 linh kiện."));
        }

        var distinctIds = new HashSet<Guid>();
        foreach (var item in itemInputs)
        {
            if (item.ProductId == Guid.Empty)
            {
                return Result.Failure<List<PCBuildItemResolved>>(Error.Validation(
                    "PCBuild.InvalidProductId",
                    "Id sản phẩm không hợp lệ."));
            }

            if (item.Quantity is < 1 or > 20)
            {
                return Result.Failure<List<PCBuildItemResolved>>(Error.Validation(
                    "PCBuild.InvalidQuantity",
                    "Số lượng linh kiện phải từ 1 đến 20."));
            }

            if (!distinctIds.Add(item.ProductId))
            {
                return Result.Failure<List<PCBuildItemResolved>>(Error.Validation(
                    "PCBuild.DuplicateProduct",
                    "Danh sách linh kiện có sản phẩm bị lặp lại. Vui lòng gộp vào số lượng."));
            }
        }

        var products = await context.Products
            .AsNoTracking()
            .Where(p => distinctIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var missingIds = distinctIds.Except(products.Keys).ToList();
        if (missingIds.Count > 0)
        {
            return Result.Failure<List<PCBuildItemResolved>>(Error.NotFound(
                "PCBuild.ProductNotFound",
                $"Một hoặc nhiều linh kiện không tồn tại trong hệ thống: {string.Join(", ", missingIds)}"));
        }

        var seenSingleChoice = new HashSet<ComponentType>();
        var resolved = new List<PCBuildItemResolved>(itemInputs.Count);

        foreach (var input in itemInputs)
        {
            var product = products[input.ProductId];

            if (SingleChoiceTypes.Contains(product.ComponentType))
            {
                if (!seenSingleChoice.Add(product.ComponentType))
                {
                    return Result.Failure<List<PCBuildItemResolved>>(Error.Validation(
                        "PCBuild.MultipleSingleChoiceComponents",
                        $"Chỉ được chọn tối đa 1 linh kiện loại {product.ComponentType}."));
                }

                if (input.Quantity != 1)
                {
                    return Result.Failure<List<PCBuildItemResolved>>(Error.Validation(
                        "PCBuild.InvalidSingleChoiceQuantity",
                        $"Linh kiện loại {product.ComponentType} bắt buộc phải có số lượng bằng 1."));
                }
            }

            resolved.Add(new PCBuildItemResolved(product, input.Quantity));
        }

        // Deterministic order: ComponentType ASC, then ProductId ASC
        return Result.Success(resolved.OrderBy(i => i.Product.ComponentType).ThenBy(i => i.Product.Id).ToList());
    }
}

using RiuTek.Application.DTOs;
using RiuTek.Core.Entities;

namespace RiuTek.Application.Common.Mappings;

public static class PCBuildMappingExtensions
{
    public static PCBuildDto ToDto(this PCBuild build) => new(
        build.Id,
        build.UserId,
        build.Name,
        build.Description,
        build.TotalPrice,
        build.EstimatedWattage,
        build.IsCompatible,
        build.CompatibilityNotes,
        build.IsAiGenerated,
        build.AiRationale,
        build.Status,
        build.Items.Select(i => i.ToDto()).ToList(),
        build.CreatedAt
    );

    public static PCBuildItemDto ToDto(this PCBuildItem item) => new(
        item.Id,
        item.ProductId,
        item.Product?.Name ?? string.Empty,
        item.Product?.ImageUrl ?? string.Empty,
        item.ComponentType,
        item.Quantity,
        item.UnitPrice,
        item.UnitPrice * item.Quantity,
        item.Product?.Specifications
    );

    public static IReadOnlyList<PCBuildDto> ToDtoList(this IEnumerable<PCBuild> builds) =>
        builds.Select(b => b.ToDto()).ToList();
}

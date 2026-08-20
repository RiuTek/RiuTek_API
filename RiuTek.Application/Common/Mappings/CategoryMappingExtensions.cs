using RiuTek.Application.DTOs;
using RiuTek.Core.Entities;

namespace RiuTek.Application.Common.Mappings;

public static class CategoryMappingExtensions
{
    public static CategoryDto ToDto(this Category category) => new(
        category.Id,
        category.Name,
        category.Slug,
        category.ComponentType,
        category.Description,
        category.ParentId,
        category.SubCategories.Select(c => c.ToDto()).ToList()
    );

    public static IReadOnlyList<CategoryDto> ToDtoList(this IEnumerable<Category> categories) =>
        categories.Select(c => c.ToDto()).ToList();
}

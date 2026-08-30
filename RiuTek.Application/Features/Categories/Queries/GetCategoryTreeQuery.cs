using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;

namespace RiuTek.Application.Features.Categories.Queries;

public record GetCategoryTreeQuery : IRequest<Result<List<CategoryDto>>>;

public class GetCategoryTreeQueryHandler : IRequestHandler<GetCategoryTreeQuery, Result<List<CategoryDto>>>
{
    private readonly IApplicationDbContext _context;

    public GetCategoryTreeQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<List<CategoryDto>>> Handle(
        GetCategoryTreeQuery request,
        CancellationToken cancellationToken)
    {
        var allCategories = await _context.Categories
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        if (allCategories.Count == 0)
        {
            return Result.Success(new List<CategoryDto>());
        }

        var categoryIdSet = allCategories.Select(c => c.Id).ToHashSet();

        // Validate that all referenced ParentIds exist in the loaded dataset
        foreach (var category in allCategories)
        {
            if (category.ParentId.HasValue && !categoryIdSet.Contains(category.ParentId.Value))
            {
                return Result.Failure<List<CategoryDto>>(Error.Validation(
                    "Category.InvalidHierarchy",
                    "Dữ liệu cây danh mục chứa danh mục mồ côi không hợp lệ."));
            }
        }

        var childrenLookup = allCategories
            .Where(c => c.ParentId.HasValue)
            .GroupBy(c => c.ParentId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList()
            );

        var roots = allCategories
            .Where(c => !c.ParentId.HasValue)
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var visitedInTree = new HashSet<Guid>();

        Result<CategoryDto> BuildNode(Category cat, HashSet<Guid> currentPath)
        {
            if (!currentPath.Add(cat.Id))
            {
                return Result.Failure<CategoryDto>(Error.Validation(
                    "Category.CycleDetected",
                    "Phát hiện vòng lặp phân cấp trong dữ liệu cây danh mục."));
            }

            visitedInTree.Add(cat.Id);
            var subDtos = new List<CategoryDto>();

            if (childrenLookup.TryGetValue(cat.Id, out var children))
            {
                foreach (var child in children)
                {
                    var childResult = BuildNode(child, new HashSet<Guid>(currentPath));
                    if (!childResult.IsSuccess)
                        return childResult;

                    subDtos.Add(childResult.Value);
                }
            }

            var dto = new CategoryDto(
                cat.Id,
                cat.Name,
                cat.Slug,
                cat.ComponentType,
                cat.Description,
                cat.ParentId,
                subDtos
            );

            return Result.Success(dto);
        }

        var tree = new List<CategoryDto>();
        foreach (var root in roots)
        {
            var nodeResult = BuildNode(root, new HashSet<Guid>());
            if (!nodeResult.IsSuccess)
            {
                return Result.Failure<List<CategoryDto>>(nodeResult.Error);
            }
            tree.Add(nodeResult.Value);
        }

        // Detect disconnected loops or orphan cycles without a root
        if (visitedInTree.Count != allCategories.Count)
        {
            return Result.Failure<List<CategoryDto>>(Error.Validation(
                "Category.InvalidHierarchy",
                "Dữ liệu cây danh mục chứa chu trình độc lập hoặc phân cấp không hợp lệ."));
        }

        return Result.Success(tree);
    }
}

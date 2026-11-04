using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Features.Products.Queries;

public static class ProductQueryExtensions
{
    public static async Task<Result<HashSet<Guid>>> GetCategoryAndDescendantIdsAsync(
        IApplicationDbContext context,
        Guid categoryId,
        CancellationToken cancellationToken)
    {
        var categoryExists = await context.Categories
            .AnyAsync(c => c.Id == categoryId, cancellationToken);

        if (!categoryExists)
        {
            return Result.Failure<HashSet<Guid>>(Error.NotFound(
                "Product.CategoryNotFound",
                "Danh mục không tồn tại."));
        }

        var allCategories = await context.Categories
            .AsNoTracking()
            .Select(c => new { c.Id, c.ParentId })
            .ToListAsync(cancellationToken);

        var descendantIds = new HashSet<Guid> { categoryId };
        var queue = new Queue<Guid>();
        queue.Enqueue(categoryId);

        var lookup = allCategories
            .Where(c => c.ParentId.HasValue)
            .GroupBy(c => c.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(c => c.Id).ToList());

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (lookup.TryGetValue(current, out var children))
            {
                foreach (var childId in children)
                {
                    if (descendantIds.Add(childId))
                    {
                        queue.Enqueue(childId);
                    }
                }
            }
        }

        return Result.Success(descendantIds);
    }

    public static IQueryable<Product> ApplyScope(
        this IQueryable<Product> query,
        ComponentType? componentType,
        HashSet<Guid>? categoryIds,
        bool? isActive,
        bool? inStock)
    {
        if (categoryIds != null && categoryIds.Count > 0)
        {
            query = query.Where(p => categoryIds.Contains(p.CategoryId));
        }

        if (componentType.HasValue)
        {
            query = query.Where(p => p.ComponentType == componentType.Value);
        }

        if (isActive.HasValue)
        {
            query = query.Where(p => p.IsActive == isActive.Value);
        }

        if (inStock.HasValue)
        {
            query = inStock.Value
                ? query.Where(p => p.StockQuantity > 0)
                : query.Where(p => p.StockQuantity == 0);
        }

        return query;
    }

    public static IQueryable<Product> ApplyFilters(
        this IQueryable<Product> query,
        ProductFilterOptions options,
        HashSet<Guid>? categoryIds = null)
    {
        if (categoryIds != null && categoryIds.Count > 0)
        {
            query = query.Where(p => categoryIds.Contains(p.CategoryId));
        }

        if (!string.IsNullOrWhiteSpace(options.SearchTerm))
        {
            var search = options.SearchTerm.Trim().ToLowerInvariant();
            query = query.Where(p =>
                p.Name.ToLower().Contains(search) ||
                p.Sku.ToLower().Contains(search) ||
                p.Brand.ToLower().Contains(search));
        }

        var effectiveBrands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(options.Brand))
        {
            effectiveBrands.Add(options.Brand.Trim());
        }

        if (options.Brands != null)
        {
            foreach (var b in options.Brands)
            {
                if (!string.IsNullOrWhiteSpace(b))
                {
                    effectiveBrands.Add(b.Trim());
                }
            }
        }

        if (effectiveBrands.Count == 1)
        {
            var brand = effectiveBrands.First().ToLowerInvariant();
            query = query.Where(p => p.Brand.Trim().ToLower() == brand);
        }
        else if (effectiveBrands.Count > 1)
        {
            var brandList = effectiveBrands.Select(b => b.ToLowerInvariant()).ToList();
            query = query.Where(p => brandList.Contains(p.Brand.Trim().ToLower()));
        }

        if (options.ComponentType.HasValue)
        {
            query = query.Where(p => p.ComponentType == options.ComponentType.Value);
        }

        if (options.MinPrice.HasValue)
        {
            query = query.Where(p => p.Price >= options.MinPrice.Value);
        }

        if (options.MaxPrice.HasValue)
        {
            query = query.Where(p => p.Price <= options.MaxPrice.Value);
        }

        if (options.InStock.HasValue)
        {
            query = options.InStock.Value
                ? query.Where(p => p.StockQuantity > 0)
                : query.Where(p => p.StockQuantity == 0);
        }

        if (options.IsActive.HasValue)
        {
            query = query.Where(p => p.IsActive == options.IsActive.Value);
        }

        return query;
    }

    public static IQueryable<Product> ApplySorting(
        this IQueryable<Product> query,
        ProductSortOption sortBy)
    {
        return sortBy switch
        {
            ProductSortOption.PriceLowToHigh => query
                .OrderBy(p => p.Price)
                .ThenBy(p => p.Name.ToLower())
                .ThenBy(p => p.Id),

            ProductSortOption.PriceHighToLow => query
                .OrderByDescending(p => p.Price)
                .ThenBy(p => p.Name.ToLower())
                .ThenBy(p => p.Id),

            ProductSortOption.NameAToZ => query
                .OrderBy(p => p.Name.ToLower())
                .ThenBy(p => p.Id),

            ProductSortOption.NameZToA => query
                .OrderByDescending(p => p.Name.ToLower())
                .ThenBy(p => p.Id),

            _ => query // Default Newest
                .OrderByDescending(p => p.CreatedAt)
                .ThenBy(p => p.Id)
        };
    }
}

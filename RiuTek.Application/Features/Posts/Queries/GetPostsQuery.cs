using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Common.Mappings;
using RiuTek.Application.Common.Models;
using RiuTek.Application.DTOs;
using RiuTek.Core.Common;

namespace RiuTek.Application.Features.Posts.Queries;

public record GetPostsQuery(
    int PageIndex = 1,
    int PageSize = 10,
    string? SearchTerm = null,
    bool? IsFeaturedOnly = null,
    bool IsPublishedOnly = true
) : IRequest<Result<PagedResult<PostSummaryDto>>>;

public class GetPostsQueryHandler : IRequestHandler<GetPostsQuery, Result<PagedResult<PostSummaryDto>>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICacheService _cacheService;

    public GetPostsQueryHandler(
        IApplicationDbContext context,
        ICacheService cacheService)
    {
        _context = context;
        _cacheService = cacheService;
    }

    public async Task<Result<PagedResult<PostSummaryDto>>> Handle(
        GetPostsQuery request,
        CancellationToken cancellationToken)
    {
        var pageIndex = request.PageIndex < 1 ? 1 : request.PageIndex;
        var pageSize = request.PageSize < 1 ? 10 : (request.PageSize > 50 ? 50 : request.PageSize);
        var searchTerm = request.SearchTerm?.Trim();

        var cacheKey = PostCacheKeys.GetListKey(
            pageIndex,
            pageSize,
            request.IsFeaturedOnly,
            request.IsPublishedOnly,
            searchTerm);

        var cachedData = await _cacheService.GetAsync<PagedResult<PostSummaryDto>>(cacheKey, cancellationToken);
        if (cachedData != null)
        {
            return Result.Success(cachedData);
        }

        var query = _context.Posts
            .AsNoTracking()
            .Include(p => p.Author)
            .AsQueryable();

        if (request.IsPublishedOnly)
        {
            query = query.Where(p => p.IsPublished);
        }

        if (request.IsFeaturedOnly.HasValue && request.IsFeaturedOnly.Value)
        {
            query = query.Where(p => p.IsFeatured);
        }

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var lowerSearchTerm = searchTerm.ToLowerInvariant();
            query = query.Where(p => p.Title.ToLower().Contains(lowerSearchTerm) || p.Summary.ToLower().Contains(lowerSearchTerm));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(p => p.PublishedAt ?? p.CreatedAt)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Select(p => p.ToSummaryDto())
            .ToListAsync(cancellationToken);

        var result = PagedResult<PostSummaryDto>.Create(items, totalCount, pageIndex, pageSize);

        await _cacheService.SetAsync(cacheKey, result, TimeSpan.FromMinutes(10), cancellationToken);

        return Result.Success(result);
    }
}

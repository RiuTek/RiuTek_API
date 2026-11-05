using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Products.Common;
using RiuTek.Core.Common;

namespace RiuTek.Application.Features.Banners.Queries;

public record GetManagedBannersQuery : IRequest<Result<List<ManagedBannerDto>>>;

public class GetManagedBannersQueryHandler : IRequestHandler<GetManagedBannersQuery, Result<List<ManagedBannerDto>>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public GetManagedBannersQueryHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result<List<ManagedBannerDto>>> Handle(
        GetManagedBannersQuery request,
        CancellationToken cancellationToken)
    {
        var authResult = await ProductMediaHelper.ValidateActiveContentManagerAsync(
            _currentUserService,
            _context,
            cancellationToken);

        if (!authResult.IsSuccess)
        {
            return Result.Failure<List<ManagedBannerDto>>(authResult.Error);
        }

        var banners = await _context.Banners
            .AsNoTracking()
            .OrderBy(b => b.DisplayOrder)
            .ThenBy(b => b.Id)
            .Select(b => new ManagedBannerDto(
                b.Id,
                b.Title,
                b.ImageObjectKey,
                b.ImageUrl,
                b.TargetUrl,
                b.DisplayOrder,
                b.IsActive,
                b.CreatedAt,
                b.UpdatedAt))
            .ToListAsync(cancellationToken);

        return Result.Success(banners);
    }
}

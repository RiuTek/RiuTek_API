using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Core.Common;

namespace RiuTek.Application.Features.Banners.Queries;

public record GetPublicBannersQuery : IRequest<Result<List<PublicBannerDto>>>;

public class GetPublicBannersQueryHandler : IRequestHandler<GetPublicBannersQuery, Result<List<PublicBannerDto>>>
{
    private readonly IApplicationDbContext _context;

    public GetPublicBannersQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<List<PublicBannerDto>>> Handle(
        GetPublicBannersQuery request,
        CancellationToken cancellationToken)
    {
        var banners = await _context.Banners
            .AsNoTracking()
            .Where(b => b.IsActive)
            .OrderBy(b => b.DisplayOrder)
            .ThenBy(b => b.Id)
            .Select(b => new PublicBannerDto(
                b.Id,
                b.Title,
                b.ImageUrl,
                b.TargetUrl,
                b.DisplayOrder))
            .ToListAsync(cancellationToken);

        return Result.Success(banners);
    }
}

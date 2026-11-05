using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Products.Common;
using RiuTek.Core.Common;

namespace RiuTek.Application.Features.Banners.Queries;

public record GetManagedBannerByIdQuery(Guid Id) : IRequest<Result<ManagedBannerDto>>;

public class GetManagedBannerByIdQueryHandler : IRequestHandler<GetManagedBannerByIdQuery, Result<ManagedBannerDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public GetManagedBannerByIdQueryHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result<ManagedBannerDto>> Handle(
        GetManagedBannerByIdQuery request,
        CancellationToken cancellationToken)
    {
        var authResult = await ProductMediaHelper.ValidateActiveContentManagerAsync(
            _currentUserService,
            _context,
            cancellationToken);

        if (!authResult.IsSuccess)
        {
            return Result.Failure<ManagedBannerDto>(authResult.Error);
        }

        var banner = await _context.Banners
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == request.Id, cancellationToken);

        if (banner == null)
        {
            return Result.Failure<ManagedBannerDto>(Error.NotFound(
                "Banner.NotFound",
                "Banner không tồn tại."));
        }

        return Result.Success(new ManagedBannerDto(
            banner.Id,
            banner.Title,
            banner.ImageObjectKey,
            banner.ImageUrl,
            banner.TargetUrl,
            banner.DisplayOrder,
            banner.IsActive,
            banner.CreatedAt,
            banner.UpdatedAt));
    }
}

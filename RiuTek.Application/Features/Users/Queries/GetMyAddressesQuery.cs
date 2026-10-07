using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Common.Mappings;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Users.Common;
using RiuTek.Core.Common;

namespace RiuTek.Application.Features.Users.Queries;

public record GetMyAddressesQuery : IRequest<Result<List<UserAddressDto>>>;

public class GetMyAddressesQueryHandler : IRequestHandler<GetMyAddressesQuery, Result<List<UserAddressDto>>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public GetMyAddressesQueryHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result<List<UserAddressDto>>> Handle(
        GetMyAddressesQuery request,
        CancellationToken cancellationToken)
    {
        var authResult = await UserAuthHelper.ValidateActiveUserAsync(
            _currentUserService,
            _context,
            cancellationToken);

        if (!authResult.IsSuccess)
        {
            return Result.Failure<List<UserAddressDto>>(authResult.Error);
        }

        var userId = authResult.Value;

        var addresses = await _context.UserAddresses
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.IsDefault)
            .ThenBy(a => a.CreatedAt)
            .ThenBy(a => a.Id)
            .ToListAsync(cancellationToken);

        return Result.Success(addresses.Select(a => a.ToDto()).ToList());
    }
}

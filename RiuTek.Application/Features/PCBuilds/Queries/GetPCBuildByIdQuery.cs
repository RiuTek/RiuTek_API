using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Common.Mappings;
using RiuTek.Application.DTOs;
using RiuTek.Core.Common;

namespace RiuTek.Application.Features.PCBuilds.Queries;

public record GetPCBuildByIdQuery(Guid Id) : IRequest<Result<PCBuildDto>>;

public class GetPCBuildByIdQueryHandler : IRequestHandler<GetPCBuildByIdQuery, Result<PCBuildDto>>
{
    private readonly IApplicationDbContext _context;

    public GetPCBuildByIdQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<PCBuildDto>> Handle(
        GetPCBuildByIdQuery request,
        CancellationToken cancellationToken)
    {
        var build = await _context.PCBuilds
            .AsNoTracking()
            .Include(b => b.Items)
                .ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(b => b.Id == request.Id, cancellationToken);

        if (build == null)
        {
            return Result.Failure<PCBuildDto>(Error.NotFound(
                "PCBuild.NotFound",
                $"Không tìm thấy cấu hình PC với Id '{request.Id}'."));
        }

        return Result.Success(build.ToDto());
    }
}

using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Core.Common;

namespace RiuTek.Application.Features.PCBuilds.Commands;

public record CheckCompatibilityCommand(List<Guid> ProductIds) : IRequest<Result<CompatibilityCheckResultDto>>;

public class CheckCompatibilityCommandHandler : IRequestHandler<CheckCompatibilityCommand, Result<CompatibilityCheckResultDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IHardwareCompatibilityService _compatibilityService;

    public CheckCompatibilityCommandHandler(
        IApplicationDbContext context,
        IHardwareCompatibilityService compatibilityService)
    {
        _context = context;
        _compatibilityService = compatibilityService;
    }

    public async Task<Result<CompatibilityCheckResultDto>> Handle(
        CheckCompatibilityCommand request,
        CancellationToken cancellationToken)
    {
        if (request.ProductIds.Count == 0)
        {
            return Result.Success(new CompatibilityCheckResultDto(
                IsCompatible: true,
                IsCompleteSystem: false,
                MissingComponents: ["CPU", "Mainboard", "RAM", "Ổ cứng (SSD/HDD)", "Nguồn (PSU)", "Vỏ Case"],
                EstimatedWattage: 0,
                RecommendedPsuWattage: 0,
                Issues: []
            ));
        }

        var products = await _context.Products
            .AsNoTracking()
            .Where(p => request.ProductIds.Contains(p.Id))
            .ToListAsync(cancellationToken);

        var result = _compatibilityService.ValidateComponents(products);

        return Result.Success(result);
    }
}

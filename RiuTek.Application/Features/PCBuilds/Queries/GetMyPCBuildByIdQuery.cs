using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.PCBuilds.Common;
using RiuTek.Core.Common;

namespace RiuTek.Application.Features.PCBuilds.Queries;

public record GetMyPCBuildByIdQuery(Guid Id) : IRequest<Result<MyPCBuildDto>>;

public class GetMyPCBuildByIdQueryValidator : AbstractValidator<GetMyPCBuildByIdQuery>
{
    public GetMyPCBuildByIdQueryValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Id cấu hình PC không hợp lệ.");
    }
}

public class GetMyPCBuildByIdQueryHandler : IRequestHandler<GetMyPCBuildByIdQuery, Result<MyPCBuildDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly IHardwareCompatibilityService _compatibilityService;

    public GetMyPCBuildByIdQueryHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        IHardwareCompatibilityService compatibilityService)
    {
        _context = context;
        _currentUserService = currentUserService;
        _compatibilityService = compatibilityService;
    }

    public async Task<Result<MyPCBuildDto>> Handle(
        GetMyPCBuildByIdQuery request,
        CancellationToken cancellationToken)
    {
        var authResult = await PCBuildAuthHelper.ValidateActiveCustomerAsync(
            _currentUserService,
            _context,
            cancellationToken);

        if (authResult.IsFailure)
        {
            return Result.Failure<MyPCBuildDto>(authResult.Error);
        }

        var customer = authResult.Value;

        var pcBuild = await _context.PCBuilds
            .AsNoTracking()
            .Include(b => b.Items)
                .ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(b => b.Id == request.Id && b.UserId == customer.Id, cancellationToken);

        if (pcBuild is null)
        {
            return Result.Failure<MyPCBuildDto>(Error.NotFound(
                "PCBuild.NotFound",
                $"Không tìm thấy cấu hình PC với Id '{request.Id}'."));
        }

        var selections = pcBuild.Items
            .Where(i => i.Product != null)
            .Select(i => new ComponentSelection(i.Product!, i.Quantity))
            .ToList();

        var compatibility = _compatibilityService.ValidateComponentSelections(selections);

        var resultDto = PCBuildResponseBuilder.BuildMyBuildDto(pcBuild, compatibility);
        return Result.Success(resultDto);
    }
}

using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Common.Models;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.PCBuilds.Common;
using RiuTek.Core.Common;

namespace RiuTek.Application.Features.PCBuilds.Queries;

public record GetMyPCBuildsQuery(
    int PageIndex = 1,
    int PageSize = 20
) : IRequest<Result<PagedResult<MyPCBuildSummaryDto>>>;

public class GetMyPCBuildsQueryValidator : AbstractValidator<GetMyPCBuildsQuery>
{
    public GetMyPCBuildsQueryValidator()
    {
        RuleFor(x => x.PageIndex)
            .GreaterThanOrEqualTo(1).WithMessage("PageIndex must be greater than or equal to 1.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 50).WithMessage("PageSize must be between 1 and 50.");
    }
}

public class GetMyPCBuildsQueryHandler : IRequestHandler<GetMyPCBuildsQuery, Result<PagedResult<MyPCBuildSummaryDto>>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly IHardwareCompatibilityService _compatibilityService;

    public GetMyPCBuildsQueryHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        IHardwareCompatibilityService compatibilityService)
    {
        _context = context;
        _currentUserService = currentUserService;
        _compatibilityService = compatibilityService;
    }

    public async Task<Result<PagedResult<MyPCBuildSummaryDto>>> Handle(
        GetMyPCBuildsQuery request,
        CancellationToken cancellationToken)
    {
        var authResult = await PCBuildAuthHelper.ValidateActiveCustomerAsync(
            _currentUserService,
            _context,
            cancellationToken);

        if (authResult.IsFailure)
        {
            return Result.Failure<PagedResult<MyPCBuildSummaryDto>>(authResult.Error);
        }

        var customer = authResult.Value;

        var query = _context.PCBuilds
            .AsNoTracking()
            .Where(b => b.UserId == customer.Id);

        var totalCount = await query.CountAsync(cancellationToken);

        long offset = ((long)request.PageIndex - 1) * request.PageSize;

        if (totalCount == 0 || offset >= totalCount || offset > int.MaxValue)
        {
            var emptyResult = PagedResult<MyPCBuildSummaryDto>.Create(
                [],
                totalCount,
                request.PageIndex,
                request.PageSize);

            return Result.Success(emptyResult);
        }

        var builds = await query
            .OrderByDescending(b => b.CreatedAt)
            .ThenByDescending(b => b.Id)
            .Skip((int)offset)
            .Take(request.PageSize)
            .Include(b => b.Items)
                .ThenInclude(i => i.Product)
            .ToListAsync(cancellationToken);

        var dtos = builds.Select(b =>
        {
            var selections = b.Items
                .Where(i => i.Product != null)
                .Select(i => new ComponentSelection(i.Product!, i.Quantity))
                .ToList();

            var compatibility = _compatibilityService.ValidateComponentSelections(selections);
            return PCBuildResponseBuilder.BuildMyBuildSummaryDto(b, compatibility);
        }).ToList();

        var pagedResult = PagedResult<MyPCBuildSummaryDto>.Create(
            dtos,
            totalCount,
            request.PageIndex,
            request.PageSize);

        return Result.Success(pagedResult);
    }
}

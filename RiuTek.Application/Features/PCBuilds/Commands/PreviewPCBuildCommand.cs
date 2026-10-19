using FluentValidation;
using MediatR;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.PCBuilds.Common;
using RiuTek.Core.Common;

namespace RiuTek.Application.Features.PCBuilds.Commands;

public record PreviewPCBuildCommand(
    List<PCBuildItemInputDto> Items
) : IRequest<Result<PCBuildPreviewResultDto>>;

public class PreviewPCBuildCommandValidator : AbstractValidator<PreviewPCBuildCommand>
{
    public PreviewPCBuildCommandValidator()
    {
        RuleFor(x => x.Items)
            .NotEmpty().WithMessage("Cấu hình PC phải có ít nhất 1 linh kiện.")
            .Must(x => x != null && x.Count <= 30).WithMessage("Cấu hình PC chỉ được chứa tối đa 30 linh kiện.");

        RuleForEach(x => x.Items).SetValidator(new PCBuildItemInputValidator());
    }
}

public class PreviewPCBuildCommandHandler : IRequestHandler<PreviewPCBuildCommand, Result<PCBuildPreviewResultDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IHardwareCompatibilityService _compatibilityService;

    public PreviewPCBuildCommandHandler(
        IApplicationDbContext context,
        IHardwareCompatibilityService compatibilityService)
    {
        _context = context;
        _compatibilityService = compatibilityService;
    }

    public async Task<Result<PCBuildPreviewResultDto>> Handle(
        PreviewPCBuildCommand request,
        CancellationToken cancellationToken)
    {
        var resolveResult = await PCBuildValidationHelper.ValidateAndResolveItemsAsync(
            request.Items,
            _context,
            cancellationToken);

        if (resolveResult.IsFailure)
        {
            return Result.Failure<PCBuildPreviewResultDto>(resolveResult.Error);
        }

        var resolvedItems = resolveResult.Value;

        var selections = resolvedItems
            .Select(r => new ComponentSelection(r.Product, r.Quantity))
            .ToList();

        var compatibility = _compatibilityService.ValidateComponentSelections(selections);

        var previewResult = PCBuildResponseBuilder.BuildPreviewResult(resolvedItems, compatibility);
        return Result.Success(previewResult);
    }
}

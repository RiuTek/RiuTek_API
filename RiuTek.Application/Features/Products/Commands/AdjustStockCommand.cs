using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Products.Common;
using RiuTek.Core.Common;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Features.Products.Commands;

public record AdjustStockCommand(
    Guid ProductId,
    int ExpectedStockQuantity,
    int QuantityDelta,
    string Note
) : IRequest<Result<StockAdjustmentResultDto>>;

public class AdjustStockCommandValidator : AbstractValidator<AdjustStockCommand>
{
    public AdjustStockCommandValidator()
    {
        RuleFor(x => x.ProductId)
            .NotEmpty().WithMessage("Mã sản phẩm không được để trống.");

        RuleFor(x => x.ExpectedStockQuantity)
            .GreaterThanOrEqualTo(0).WithMessage("Số lượng tồn kho kỳ vọng không được âm.");

        RuleFor(x => x.QuantityDelta)
            .NotEqual(0).WithMessage("Biến động tồn kho không được bằng 0.");

        RuleFor(x => x.Note)
            .NotEmpty().WithMessage("Ghi chú lý do điều chỉnh kho không được để trống.")
            .Must(note => !string.IsNullOrWhiteSpace(note) && note.Trim().Length is >= 3 and <= 500)
            .WithMessage("Ghi chú lý do điều chỉnh kho phải từ 3 đến 500 ký tự.");
    }
}

public class AdjustStockCommandHandler : IRequestHandler<AdjustStockCommand, Result<StockAdjustmentResultDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public AdjustStockCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result<StockAdjustmentResultDto>> Handle(
        AdjustStockCommand request,
        CancellationToken cancellationToken)
    {
        var authResult = await StockAuthHelper.ValidateActiveContentManagerAsync(
            _currentUserService,
            _context,
            cancellationToken);

        if (authResult.IsFailure)
        {
            return Result.Failure<StockAdjustmentResultDto>(authResult.Error);
        }

        var actorUser = authResult.Value;

        var product = await _context.Products
            .FirstOrDefaultAsync(p => p.Id == request.ProductId, cancellationToken);

        if (product == null)
        {
            return Result.Failure<StockAdjustmentResultDto>(Error.NotFound(
                "Product.NotFound",
                "Không tìm thấy sản phẩm."));
        }

        if (product.StockQuantity != request.ExpectedStockQuantity)
        {
            return Result.Failure<StockAdjustmentResultDto>(Error.Conflict(
                "Inventory.StockConflict",
                $"Số lượng tồn kho kỳ vọng ({request.ExpectedStockQuantity}) không khớp với tồn kho hiện tại ({product.StockQuantity})."));
        }

        var movementResult = StockMovementHelper.ApplyStockMovement(
            product,
            request.QuantityDelta,
            StockMovementReason.ManualAdjustment,
            request.Note,
            actorUser.Id,
            orderId: null,
            _context);

        if (movementResult.IsFailure)
        {
            return Result.Failure<StockAdjustmentResultDto>(movementResult.Error);
        }

        var movement = movementResult.Value;

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<StockAdjustmentResultDto>(Error.Conflict(
                "Inventory.StockConflict",
                "Đã có xung đột thay đổi tồn kho cùng thời điểm. Vui lòng tải lại và thử lại."));
        }

        var dto = new StockAdjustmentResultDto(
            ProductId: product.Id,
            Sku: product.Sku,
            StockBefore: movement.StockBefore,
            QuantityDelta: movement.QuantityDelta,
            StockAfter: movement.StockAfter,
            MovementId: movement.Id,
            Reason: movement.Reason,
            Note: movement.Note,
            CreatedAt: movement.CreatedAt);

        return Result.Success(dto);
    }
}

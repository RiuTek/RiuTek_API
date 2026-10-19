using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Features.PCBuilds.Common;
using RiuTek.Core.Common;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Features.PCBuilds.Commands;

public record DeleteMyPCBuildCommand(Guid Id) : IRequest<Result>;

public class DeleteMyPCBuildCommandValidator : AbstractValidator<DeleteMyPCBuildCommand>
{
    public DeleteMyPCBuildCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Id cấu hình PC không hợp lệ.");
    }
}

public class DeleteMyPCBuildCommandHandler : IRequestHandler<DeleteMyPCBuildCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public DeleteMyPCBuildCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result> Handle(
        DeleteMyPCBuildCommand request,
        CancellationToken cancellationToken)
    {
        var authResult = await PCBuildAuthHelper.ValidateActiveCustomerAsync(
            _currentUserService,
            _context,
            cancellationToken);

        if (authResult.IsFailure)
        {
            return Result.Failure(authResult.Error);
        }

        var customer = authResult.Value;

        var pcBuild = await _context.PCBuilds
            .FirstOrDefaultAsync(b => b.Id == request.Id && b.UserId == customer.Id, cancellationToken);

        if (pcBuild is null)
        {
            return Result.Failure(Error.NotFound(
                "PCBuild.NotFound",
                $"Không tìm thấy cấu hình PC với Id '{request.Id}'."));
        }

        if (pcBuild.Status == PCBuildStatus.Ordered)
        {
            return Result.Failure(Error.Conflict(
                "PCBuild.CannotDeleteOrderedBuild",
                "Không thể xóa cấu hình PC đã đặt hàng."));
        }

        var isReferencedByOrder = await _context.OrderItems
            .AnyAsync(oi => oi.PCBuildId == pcBuild.Id, cancellationToken);

        if (isReferencedByOrder)
        {
            return Result.Failure(Error.Conflict(
                "PCBuild.CannotDeleteOrderedBuild",
                "Không thể xóa cấu hình PC đã đặt hàng."));
        }

        _context.PCBuilds.Remove(pcBuild);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            _context.ClearTrackedChanges();
            return Result.Failure(Error.Conflict(
                "PCBuild.ConcurrencyConflict",
                "Cấu hình PC đã bị thay đổi hoặc đã bị xóa bởi thao tác khác."));
        }

        return Result.Success();
    }
}

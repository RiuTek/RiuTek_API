using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Features.Products.Common;
using RiuTek.Core.Common;

namespace RiuTek.Application.Features.Banners.Commands;

public record DeleteBannerCommand(Guid Id) : IRequest<Result>;

public class DeleteBannerCommandValidator : AbstractValidator<DeleteBannerCommand>
{
    public DeleteBannerCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Id banner không được để trống.");
    }
}

public class DeleteBannerCommandHandler : IRequestHandler<DeleteBannerCommand, Result>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public DeleteBannerCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result> Handle(
        DeleteBannerCommand request,
        CancellationToken cancellationToken)
    {
        var authResult = await ProductMediaHelper.ValidateActiveContentManagerAsync(
            _currentUserService,
            _context,
            cancellationToken);

        if (!authResult.IsSuccess)
        {
            return Result.Failure(authResult.Error);
        }

        var banner = await _context.Banners
            .FirstOrDefaultAsync(b => b.Id == request.Id, cancellationToken);

        if (banner == null)
        {
            return Result.Failure(Error.NotFound(
                "Banner.NotFound",
                "Banner không tồn tại."));
        }

        _context.Banners.Remove(banner);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

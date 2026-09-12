using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Carts.Common;
using RiuTek.Core.Common;

namespace RiuTek.Application.Features.Carts.Queries;

public record GetCartQuery : IRequest<Result<CartDto>>;

public class GetCartQueryHandler : IRequestHandler<GetCartQuery, Result<CartDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public GetCartQueryHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result<CartDto>> Handle(
        GetCartQuery request,
        CancellationToken cancellationToken)
    {
        var authResult = await CartAuthHelper.ValidateActiveUserAsync(_currentUserService, _context, cancellationToken);
        if (authResult.IsFailure)
        {
            return authResult.Error;
        }

        var userId = authResult.Value;

        var cart = await _context.Carts
            .AsNoTracking()
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);

        if (cart is null)
        {
            return Result<CartDto>.Success(CartResponseBuilder.BuildEmpty());
        }

        var productIds = cart.Items.Select(i => i.ProductId);
        var productsMap = await CartResponseBuilder.GetProductsMapAsync(_context, productIds, cancellationToken);

        return Result<CartDto>.Success(CartResponseBuilder.Build(cart, productsMap));
    }
}

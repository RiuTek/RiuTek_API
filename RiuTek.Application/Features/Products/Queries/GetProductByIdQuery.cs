using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Authorization;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Common.Mappings;
using RiuTek.Application.DTOs;
using RiuTek.Core.Common;

namespace RiuTek.Application.Features.Products.Queries;

public record GetProductByIdQuery(Guid Id)
    : IRequest<Result<ProductDto>>;

public class GetProductByIdQueryValidator : AbstractValidator<GetProductByIdQuery>
{
    public GetProductByIdQueryValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id sản phẩm không hợp lệ.");
    }
}

public class GetProductByIdQueryHandler : IRequestHandler<GetProductByIdQuery, Result<ProductDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public GetProductByIdQueryHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result<ProductDto>> Handle(
        GetProductByIdQuery request,
        CancellationToken cancellationToken)
    {
        if (!_currentUserService.IsAuthenticated || _currentUserService.UserId == null)
        {
            return Result.Failure<ProductDto>(Error.Unauthorized(
                "Auth.Unauthorized",
                "Bạn cần đăng nhập để xem thông tin sản phẩm."));
        }

        if (!AuthorizationRules.IsContentManager(_currentUserService.UserRole))
        {
            return Result.Failure<ProductDto>(Error.Forbidden(
                "Product.Forbidden",
                "Bạn không có quyền xem thông tin chi tiết quản trị sản phẩm."));
        }

        var product = await _context.Products
            .AsNoTracking()
            .Where(p => p.Id == request.Id)
            .Select(ProductMappingExtensions.ToDtoProjection)
            .FirstOrDefaultAsync(cancellationToken);

        if (product == null)
        {
            return Result.Failure<ProductDto>(Error.NotFound(
                "Product.NotFound",
                "Không tìm thấy sản phẩm."));
        }

        return Result.Success(product);
    }
}

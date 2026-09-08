using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Common.Mappings;
using RiuTek.Application.DTOs;
using RiuTek.Core.Common;

namespace RiuTek.Application.Features.Products.Queries;

public record GetProductBySlugQuery(string Slug)
    : IRequest<Result<ProductDto>>;

public class GetProductBySlugQueryValidator : AbstractValidator<GetProductBySlugQuery>
{
    public GetProductBySlugQueryValidator()
    {
        RuleFor(x => x.Slug)
            .NotEmpty()
            .WithMessage("Slug sản phẩm không được để trống.")
            .MaximumLength(255)
            .WithMessage("Slug sản phẩm không được vượt quá 255 ký tự.");
    }
}

public class GetProductBySlugQueryHandler : IRequestHandler<GetProductBySlugQuery, Result<ProductDto>>
{
    private readonly IApplicationDbContext _context;

    public GetProductBySlugQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<ProductDto>> Handle(
        GetProductBySlugQuery request,
        CancellationToken cancellationToken)
    {
        var normalizedSlug = request.Slug.Trim().ToLowerInvariant();

        var product = await _context.Products
            .AsNoTracking()
            .Where(p => p.Slug.ToLower() == normalizedSlug)
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

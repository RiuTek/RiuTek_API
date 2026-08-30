using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Common.Mappings;
using RiuTek.Application.DTOs;
using RiuTek.Core.Common;

namespace RiuTek.Application.Features.Comments.Queries;

public record GetProductCommentsQuery(Guid ProductId) : IRequest<Result<List<ProductCommentDto>>>;

public class GetProductCommentsQueryHandler : IRequestHandler<GetProductCommentsQuery, Result<List<ProductCommentDto>>>
{
    private readonly IApplicationDbContext _context;

    public GetProductCommentsQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Result<List<ProductCommentDto>>> Handle(
        GetProductCommentsQuery request,
        CancellationToken cancellationToken)
    {
        var productExistsAndActive = await _context.Products
            .AnyAsync(p => p.Id == request.ProductId && p.IsActive, cancellationToken);

        if (!productExistsAndActive)
        {
            return Result.Failure<List<ProductCommentDto>>(Error.NotFound(
                "Product.NotFound",
                "Sản phẩm không tồn tại."));
        }

        var comments = await _context.Comments
            .AsNoTracking()
            .Include(c => c.User)
            .Include(c => c.Replies)
                .ThenInclude(r => r.User)
            .Where(c => c.ProductId == request.ProductId && c.ParentCommentId == null)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(cancellationToken);

        var result = comments.Select(c => c.ToProductDto()).ToList();

        return Result.Success(result);
    }
}

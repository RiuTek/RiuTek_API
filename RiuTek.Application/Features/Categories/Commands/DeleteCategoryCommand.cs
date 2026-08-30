using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Authorization;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Core.Common;

namespace RiuTek.Application.Features.Categories.Commands;

public record DeleteCategoryCommand(Guid Id) : IRequest<Result<Unit>>;

public class DeleteCategoryCommandHandler : IRequestHandler<DeleteCategoryCommand, Result<Unit>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public DeleteCategoryCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public async Task<Result<Unit>> Handle(
        DeleteCategoryCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUserService.IsAuthenticated || _currentUserService.UserId == null)
        {
            return Result.Failure<Unit>(Error.Unauthorized(
                "Auth.Unauthorized",
                "Bạn cần đăng nhập để xóa danh mục."));
        }

        if (!AuthorizationRules.IsContentManager(_currentUserService.UserRole))
        {
            return Result.Failure<Unit>(Error.Forbidden(
                "Category.Forbidden",
                "Bạn không có quyền xóa danh mục."));
        }

        var category = await _context.Categories
            .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken);

        if (category == null)
        {
            return Result.Failure<Unit>(Error.NotFound(
                "Category.NotFound",
                "Không tìm thấy danh mục cần xóa."));
        }

        var hasSubCategories = await _context.Categories
            .AnyAsync(c => c.ParentId == request.Id, cancellationToken);

        if (hasSubCategories)
        {
            return Result.Failure<Unit>(Error.Conflict(
                "Category.HasSubCategories",
                "Không thể xóa danh mục đang có danh mục con."));
        }

        var hasProducts = await _context.Products
            .AnyAsync(p => p.CategoryId == request.Id, cancellationToken);

        if (hasProducts)
        {
            return Result.Failure<Unit>(Error.Conflict(
                "Category.HasProducts",
                "Không thể xóa danh mục đang chứa sản phẩm."));
        }

        _context.Categories.Remove(category);
        await _context.SaveChangesAsync(cancellationToken);

        return Result.Success(Unit.Value);
    }
}

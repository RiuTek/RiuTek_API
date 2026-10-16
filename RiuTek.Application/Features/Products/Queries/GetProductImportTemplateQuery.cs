using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Products.Common;
using RiuTek.Core.Common;

namespace RiuTek.Application.Features.Products.Queries;

public record GetProductImportTemplateQuery : IRequest<Result<ProductImportTemplateDto>>;

public class GetProductImportTemplateQueryHandler : IRequestHandler<GetProductImportTemplateQuery, Result<ProductImportTemplateDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly IProductImportWorkbookService _workbookService;

    public GetProductImportTemplateQueryHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        IProductImportWorkbookService workbookService)
    {
        _context = context;
        _currentUserService = currentUserService;
        _workbookService = workbookService;
    }

    public async Task<Result<ProductImportTemplateDto>> Handle(
        GetProductImportTemplateQuery request,
        CancellationToken cancellationToken)
    {
        var authResult = await ProductMediaHelper.ValidateActiveContentManagerAsync(
            _currentUserService,
            _context,
            cancellationToken);

        if (!authResult.IsSuccess)
        {
            return Result.Failure<ProductImportTemplateDto>(authResult.Error);
        }

        var categories = await _context.Categories.AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new CategoryLookupDto(c.Slug, c.Name, c.ComponentType))
            .ToListAsync(cancellationToken);

        var template = await _workbookService.GenerateTemplateAsync(categories, cancellationToken);
        return Result.Success(template);
    }
}

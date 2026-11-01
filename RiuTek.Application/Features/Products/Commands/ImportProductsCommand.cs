using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Common.Utils;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Products.Common;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Features.Products.Commands;

public record ImportProductsCommand(
    Stream FileStream,
    string FileName,
    long FileLength,
    string? ImageMappingsJson = null
) : IRequest<Result<ProductImportCommitResultDto>>;

public class ImportProductsCommandHandler : IRequestHandler<ImportProductsCommand, Result<ProductImportCommitResultDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly IProductImportWorkbookService _workbookService;
    private readonly ISender _sender;
    private readonly ICurrentUserService _currentUserService;

    public ImportProductsCommandHandler(
        IApplicationDbContext context,
        IProductImportWorkbookService workbookService,
        ISender sender,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _workbookService = workbookService;
        _sender = sender;
        _currentUserService = currentUserService;
    }

    public async Task<Result<ProductImportCommitResultDto>> Handle(
        ImportProductsCommand request,
        CancellationToken cancellationToken)
    {
        await using var bufferedStream = new MemoryStream();
        await request.FileStream.CopyToAsync(bufferedStream, cancellationToken);

        bufferedStream.Position = 0;
        var validateCommand = new ValidateProductImportCommand(
            bufferedStream,
            request.FileName,
            request.FileLength,
            request.ImageMappingsJson);

        var validationResult = await _sender.Send(validateCommand, cancellationToken);

        if (!validationResult.IsSuccess)
        {
            return Result.Failure<ProductImportCommitResultDto>(validationResult.Error);
        }

        var validation = validationResult.Value;
        if (!validation.IsValid)
        {
            return Result.Success(new ProductImportCommitResultDto(
                validation.TemplateVersion,
                IsCommitted: false,
                CreatedCount: 0,
                CreatedProducts: [],
                validation));
        }

        ProductImportParsedWorkbook parsed;
        if (validateCommand.ResolvedWorkbook is not null)
        {
            parsed = validateCommand.ResolvedWorkbook;
        }
        else
        {
            bufferedStream.Position = 0;
            var parseResult = await _workbookService.ParseWorkbookAsync(bufferedStream, cancellationToken);
            if (!parseResult.IsSuccess)
            {
                return Result.Failure<ProductImportCommitResultDto>(parseResult.Error);
            }

            parsed = parseResult.Value;
        }
        var categorySlugs = parsed.Rows
            .Select(row => row.CategorySlug.Trim().ToLowerInvariant())
            .Distinct()
            .ToList();

        var categories = await _context.Categories
            .Where(category => categorySlugs.Contains(category.Slug.ToLower()))
            .ToListAsync(cancellationToken);

        var categoryMap = categories.ToDictionary(
            category => category.Slug,
            StringComparer.OrdinalIgnoreCase);

        var products = new List<Product>(parsed.Rows.Count);
        foreach (var row in parsed.Rows)
        {
            if (!categoryMap.TryGetValue(row.CategorySlug.Trim(), out var category) ||
                row.Specification is null)
            {
                _context.ClearTrackedChanges();
                return Result.Failure<ProductImportCommitResultDto>(Error.Conflict(
                    "ProductImport.StaleValidation",
                    "Danh mục hoặc dữ liệu workbook đã thay đổi. Vui lòng validate lại file trước khi import."));
            }

            var normalizedName = row.Name.Trim();
            var normalizedPrimaryImage = row.ImageUrl.Trim();
            var product = new Product(
                categoryId: category.Id,
                name: normalizedName,
                slug: SlugHelper.GenerateSlug(normalizedName),
                sku: row.Sku.Trim().ToUpperInvariant(),
                brand: row.Brand.Trim(),
                price: row.Price,
                stockQuantity: row.StockQuantity,
                imageUrl: normalizedPrimaryImage,
                componentType: row.ComponentType,
                specifications: row.Specification,
                originalPrice: row.OriginalPrice)
            {
                AdditionalImages = ProductMediaHelper.NormalizeAdditionalImages(
                    normalizedPrimaryImage,
                    row.AdditionalImages),
                Category = category,
                Embedding = null,
                IsActive = true
            };

            products.Add(product);
        }

        _context.Products.AddRange(products);

        foreach (var product in products)
        {
            if (product.StockQuantity > 0)
            {
                var movement = new StockMovement(
                    productId: product.Id,
                    quantityDelta: product.StockQuantity,
                    stockBefore: 0,
                    stockAfter: product.StockQuantity,
                    reason: StockMovementReason.BulkImport,
                    note: null,
                    actorUserId: _currentUserService.UserId,
                    orderId: null);
                _context.StockMovements.Add(movement);
            }
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (_context.IsUniqueViolation(ex))
        {
            _context.ClearTrackedChanges();
            return Result.Failure<ProductImportCommitResultDto>(Error.Conflict(
                "ProductImport.ConcurrentConflict",
                "SKU hoặc slug vừa được tạo bởi yêu cầu khác. Không có sản phẩm nào trong batch được import."));
        }

        var createdProducts = products
            .Select(product => new ProductImportCreatedProductDto(
                product.Id,
                product.Sku,
                product.Slug))
            .ToList();

        return Result.Success(new ProductImportCommitResultDto(
            parsed.TemplateVersion,
            IsCommitted: true,
            CreatedCount: createdProducts.Count,
            CreatedProducts: createdProducts,
            validation));
    }
}

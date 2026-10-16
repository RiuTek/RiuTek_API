using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Common.Utils;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Products.Common;
using RiuTek.Core.Common;

namespace RiuTek.Application.Features.Products.Commands;

public record ValidateProductImportCommand(
    Stream FileStream,
    string FileName,
    long FileLength
) : IRequest<Result<ProductImportValidationResultDto>>;

public class ValidateProductImportCommandValidator : AbstractValidator<ValidateProductImportCommand>
{
    private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MiB = 5242880 bytes

    public ValidateProductImportCommandValidator()
    {
        RuleFor(x => x.FileLength)
            .GreaterThan(0).WithMessage("File upload không được để trống.")
            .LessThanOrEqualTo(MaxFileSizeBytes).WithMessage("Dung lượng file không được vượt quá 5 MiB.");

        RuleFor(x => x.FileName)
            .NotEmpty().WithMessage("Tên file không được để trống.")
            .Must(fileName => string.Equals(Path.GetExtension(fileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Chỉ chấp nhận file định dạng .xlsx.");
    }
}

public class ValidateProductImportCommandHandler : IRequestHandler<ValidateProductImportCommand, Result<ProductImportValidationResultDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly IProductImportWorkbookService _workbookService;

    public ValidateProductImportCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        IProductImportWorkbookService workbookService)
    {
        _context = context;
        _currentUserService = currentUserService;
        _workbookService = workbookService;
    }

    public async Task<Result<ProductImportValidationResultDto>> Handle(
        ValidateProductImportCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Auth and active ContentManager check
        var authResult = await ProductMediaHelper.ValidateActiveContentManagerAsync(
            _currentUserService,
            _context,
            cancellationToken);

        if (!authResult.IsSuccess)
        {
            return Result.Failure<ProductImportValidationResultDto>(authResult.Error);
        }

        // 2. File metadata validation
        if (request.FileLength <= 0)
        {
            return Result.Failure<ProductImportValidationResultDto>(Error.Validation(
                "ProductImport.MissingFile",
                "File upload không được để trống."));
        }

        if (request.FileLength > 5 * 1024 * 1024)
        {
            return Result.Failure<ProductImportValidationResultDto>(Error.Validation(
                "ProductImport.FileTooLarge",
                "Dung lượng file không được vượt quá 5 MiB."));
        }

        var extension = Path.GetExtension(request.FileName);
        if (!string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<ProductImportValidationResultDto>(Error.Validation(
                "ProductImport.InvalidFileExtension",
                "Chỉ chấp nhận file định dạng .xlsx."));
        }

        // 3. Parse workbook and initial schema / cell / cross-sheet linking checks
        var parseResult = await _workbookService.ParseWorkbookAsync(request.FileStream, cancellationToken);
        if (!parseResult.IsSuccess)
        {
            return Result.Failure<ProductImportValidationResultDto>(parseResult.Error);
        }

        var parsed = parseResult.Value;
        var allErrors = new List<ProductImportErrorDto>(parsed.ParseErrors);
        var invalidProductRows = new HashSet<int>();

        // Map existing parse errors to invalid product rows
        var skuToProductRow = parsed.Rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Sku))
            .GroupBy(r => r.Sku.Trim().ToUpperInvariant(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().RowNumber, StringComparer.OrdinalIgnoreCase);

        foreach (var error in parsed.ParseErrors)
        {
            if (string.Equals(error.Sheet, "Products", StringComparison.OrdinalIgnoreCase) && error.Row >= 2)
            {
                invalidProductRows.Add(error.Row);
            }
            else if (!string.IsNullOrWhiteSpace(error.Sku) &&
                     skuToProductRow.TryGetValue(error.Sku.Trim().ToUpperInvariant(), out var pRow))
            {
                invalidProductRows.Add(pRow);
            }
        }

        // 4. In-workbook uniqueness checks (SKU and Slug)
        var skuGroups = parsed.Rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Sku))
            .GroupBy(r => r.Sku.Trim().ToUpperInvariant(), StringComparer.OrdinalIgnoreCase);

        foreach (var group in skuGroups)
        {
            if (group.Count() > 1)
            {
                foreach (var duplicateRow in group)
                {
                    allErrors.Add(new ProductImportErrorDto(
                        "Products",
                        duplicateRow.RowNumber,
                        "SKU",
                        duplicateRow.Sku,
                        "ProductImport.DuplicateSku",
                        $"Mã SKU '{duplicateRow.Sku}' bị trùng lặp trong file."));
                    invalidProductRows.Add(duplicateRow.RowNumber);
                }
            }
        }

        var rowSlugs = new Dictionary<int, string>();
        foreach (var row in parsed.Rows)
        {
            var slug = SlugHelper.GenerateSlug(row.Name);
            if (string.IsNullOrWhiteSpace(slug))
            {
                allErrors.Add(new ProductImportErrorDto(
                    "Products",
                    row.RowNumber,
                    "Name",
                    row.Sku,
                    "ProductImport.SlugConflict",
                    "Không thể tạo slug từ tên sản phẩm."));
                invalidProductRows.Add(row.RowNumber);
            }
            else
            {
                rowSlugs[row.RowNumber] = slug;
            }
        }

        var slugGroups = parsed.Rows
            .Where(r => rowSlugs.ContainsKey(r.RowNumber))
            .GroupBy(r => rowSlugs[r.RowNumber].ToLowerInvariant(), StringComparer.OrdinalIgnoreCase);

        foreach (var group in slugGroups)
        {
            if (group.Count() > 1)
            {
                foreach (var duplicateRow in group)
                {
                    allErrors.Add(new ProductImportErrorDto(
                        "Products",
                        duplicateRow.RowNumber,
                        "Name",
                        duplicateRow.Sku,
                        "ProductImport.SlugConflict",
                        $"Tên sản phẩm tạo ra slug '{rowSlugs[duplicateRow.RowNumber]}' bị trùng lặp với dòng khác trong file."));
                    invalidProductRows.Add(duplicateRow.RowNumber);
                }
            }
        }

        // 5. Batch database checks (AsNoTracking - zero writes, no N+1 queries)
        var distinctCategorySlugs = parsed.Rows
            .Select(r => r.CategorySlug?.Trim().ToLowerInvariant())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct()
            .ToList();

        var categoriesInDb = await _context.Categories.AsNoTracking()
            .Where(c => distinctCategorySlugs.Contains(c.Slug.ToLower()))
            .ToListAsync(cancellationToken);

        var categoryMap = categoriesInDb
            .GroupBy(c => c.Slug, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var distinctSkusUpper = parsed.Rows
            .Select(r => r.Sku?.Trim().ToUpperInvariant())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct()
            .ToList();

        var existingDbSkus = await _context.Products.AsNoTracking()
            .Where(p => distinctSkusUpper.Contains(p.Sku.ToUpper()))
            .Select(p => p.Sku.ToUpper())
            .ToListAsync(cancellationToken);

        var dbSkuSet = new HashSet<string>(existingDbSkus, StringComparer.OrdinalIgnoreCase);

        var distinctSlugsLower = rowSlugs.Values
            .Select(s => s.ToLowerInvariant())
            .Distinct()
            .ToList();

        var existingDbSlugs = await _context.Products.AsNoTracking()
            .Where(p => distinctSlugsLower.Contains(p.Slug.ToLower()))
            .Select(p => p.Slug.ToLower())
            .ToListAsync(cancellationToken);

        var dbSlugSet = new HashSet<string>(existingDbSlugs, StringComparer.OrdinalIgnoreCase);

        // 6. Category, DB SKU & Slug conflicts check per row
        var cmdValidator = new CreateProductCommandValidator();

        foreach (var row in parsed.Rows)
        {
            // Category check
            var trimmedCatSlug = row.CategorySlug?.Trim() ?? string.Empty;
            RiuTek.Core.Entities.Category? category = null;
            if (string.IsNullOrWhiteSpace(trimmedCatSlug) || !categoryMap.TryGetValue(trimmedCatSlug, out category))
            {
                allErrors.Add(new ProductImportErrorDto(
                    "Products",
                    row.RowNumber,
                    "CategorySlug",
                    row.Sku,
                    "ProductImport.CategoryNotFound",
                    $"Danh mục với slug '{row.CategorySlug}' không tồn tại trong hệ thống."));
                invalidProductRows.Add(row.RowNumber);
            }
            else if (category.ComponentType != row.ComponentType)
            {
                allErrors.Add(new ProductImportErrorDto(
                    "Products",
                    row.RowNumber,
                    "CategorySlug",
                    row.Sku,
                    "ProductImport.CategoryTypeMismatch",
                    $"Loại linh kiện của danh mục ({category.ComponentType}) không khớp với loại linh kiện của sản phẩm ({row.ComponentType})."));
                invalidProductRows.Add(row.RowNumber);
            }

            // DB SKU check
            if (!string.IsNullOrWhiteSpace(row.Sku) && dbSkuSet.Contains(row.Sku.Trim().ToUpperInvariant()))
            {
                allErrors.Add(new ProductImportErrorDto(
                    "Products",
                    row.RowNumber,
                    "SKU",
                    row.Sku,
                    "ProductImport.SkuConflict",
                    $"Mã SKU '{row.Sku}' đã tồn tại trong cơ sở dữ liệu."));
                invalidProductRows.Add(row.RowNumber);
            }

            // DB Slug check
            if (rowSlugs.TryGetValue(row.RowNumber, out var generatedSlug) && dbSlugSet.Contains(generatedSlug.ToLowerInvariant()))
            {
                allErrors.Add(new ProductImportErrorDto(
                    "Products",
                    row.RowNumber,
                    "Name",
                    row.Sku,
                    "ProductImport.SlugConflict",
                    $"Tên sản phẩm tạo ra slug '{generatedSlug}' đã tồn tại trong cơ sở dữ liệu."));
                invalidProductRows.Add(row.RowNumber);
            }

            // 7. Domain Validator Reuse
            if (row.Specification != null)
            {
                var fakeCategoryId = category?.Id ?? Guid.NewGuid();
                var commandDraft = new CreateProductCommand(
                    fakeCategoryId,
                    row.Name,
                    row.Sku,
                    row.Brand,
                    row.Price,
                    row.OriginalPrice,
                    row.StockQuantity,
                    row.ImageUrl,
                    null,
                    row.ComponentType,
                    row.Specification);

                var validationResult = await cmdValidator.ValidateAsync(commandDraft, cancellationToken);
                if (!validationResult.IsValid)
                {
                    foreach (var failure in validationResult.Errors)
                    {
                        if (failure.PropertyName == "CategoryId")
                        {
                            // Already checked via categoryMap
                            continue;
                        }

                        string sheet;
                        int rowNum;
                        string col;
                        string code;

                        if (failure.PropertyName.StartsWith("Specifications.", StringComparison.OrdinalIgnoreCase) ||
                            failure.PropertyName == "Specifications")
                        {
                            sheet = row.SpecSheetName ?? row.ComponentType.ToString();
                            rowNum = row.SpecRowNumber ?? row.RowNumber;
                            col = failure.PropertyName.Replace("Specifications.", "");
                            code = "ProductImport.InvalidSpecification";
                        }
                        else
                        {
                            sheet = "Products";
                            rowNum = row.RowNumber;
                            col = failure.PropertyName;
                            code = failure.PropertyName switch
                            {
                                "ImageUrl" => "ProductImport.InvalidImageUrl",
                                "Price" or "OriginalPrice" => "ProductImport.InvalidPrice",
                                "StockQuantity" => "ProductImport.InvalidStock",
                                "Sku" => "ProductImport.InvalidSku",
                                "Name" => "ProductImport.InvalidName",
                                "Brand" => "ProductImport.InvalidBrand",
                                _ => "ProductImport.Validation"
                            };
                        }

                        allErrors.Add(new ProductImportErrorDto(
                            sheet,
                            rowNum,
                            col,
                            row.Sku,
                            code,
                            failure.ErrorMessage));
                        invalidProductRows.Add(row.RowNumber);
                    }
                }
            }
        }

        // 8. Order errors stably: Sheet -> Row -> Column -> Code
        var orderedErrors = allErrors
            .DistinctBy(e => new { e.Sheet, e.Row, e.Column, e.Sku, e.Code, e.Message })
            .OrderBy(e => e.Sheet, StringComparer.Ordinal)
            .ThenBy(e => e.Row)
            .ThenBy(e => e.Column ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(e => e.Code, StringComparer.Ordinal)
            .ToList();

        var errorsTruncated = orderedErrors.Count > 500;
        var returnedErrors = orderedErrors.Take(500).ToList();

        var totalRows = parsed.TotalProductRows;
        var invalidRowsCount = Math.Min(invalidProductRows.Count, totalRows);
        var validRowsCount = Math.Max(0, totalRows - invalidRowsCount);
        var isValid = orderedErrors.Count == 0;

        return Result.Success(new ProductImportValidationResultDto(
            parsed.TemplateVersion,
            isValid,
            totalRows,
            validRowsCount,
            invalidRowsCount,
            errorsTruncated,
            returnedErrors));
    }
}

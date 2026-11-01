using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
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
    long FileLength,
    string? ImageMappingsJson = null
) : IRequest<Result<ProductImportValidationResultDto>>
{
    public ProductImportParsedWorkbook? ResolvedWorkbook { get; set; }
}

public record InspectProductImportCommand(
    Stream FileStream,
    string FileName,
    long FileLength
) : IRequest<Result<ProductImportInspectResultDto>>;

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

public class InspectProductImportCommandValidator : AbstractValidator<InspectProductImportCommand>
{
    private const long MaxFileSizeBytes = 5 * 1024 * 1024;

    public InspectProductImportCommandValidator()
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

public class InspectProductImportCommandHandler : IRequestHandler<InspectProductImportCommand, Result<ProductImportInspectResultDto>>
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly IProductImportWorkbookService _workbookService;

    public InspectProductImportCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        IProductImportWorkbookService workbookService)
    {
        _context = context;
        _currentUserService = currentUserService;
        _workbookService = workbookService;
    }

    public async Task<Result<ProductImportInspectResultDto>> Handle(
        InspectProductImportCommand request,
        CancellationToken cancellationToken)
    {
        var handler = new ValidateProductImportCommandHandler(
            _context,
            _currentUserService,
            _workbookService);

        var validateCmd = new ValidateProductImportCommand(
            request.FileStream,
            request.FileName,
            request.FileLength);

        var validationResult = await handler.ExecuteCoreAsync(
            validateCmd,
            isInspectMode: true,
            cancellationToken);

        if (!validationResult.IsSuccess)
        {
            return Result.Failure<ProductImportInspectResultDto>(validationResult.Error);
        }

        var parsed = validateCmd.ResolvedWorkbook!;
        var val = validationResult.Value;

        var inspectRows = parsed.Rows
            .Select(r => new ProductImportInspectRowDto(
                "Products",
                r.RowNumber,
                r.Sku,
                r.Name,
                r.ComponentType,
                r.PrimaryImageFile,
                string.IsNullOrWhiteSpace(r.ImageUrl) ? null : r.ImageUrl))
            .ToList();

        return Result.Success(new ProductImportInspectResultDto(
            val.TemplateVersion,
            val.IsValid,
            inspectRows,
            val.Errors,
            val.ErrorsTruncated));
    }
}

public class ValidateProductImportCommandHandler : IRequestHandler<ValidateProductImportCommand, Result<ProductImportValidationResultDto>>
{
    private const long MaxFileSizeBytes = 5 * 1024 * 1024;
    private const int MaxImageMappingsJsonBytes = 512 * 1024; // 512 KiB
    private const int MaxImageMappingsCount = 1000;
    private const int MaxTotalObjectKeysCount = 11000;
    private const int MaxAdditionalImagesPerProduct = 10;
    private const int MaxMetadataHeadConcurrency = 3;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly IProductImportWorkbookService _workbookService;
    private readonly IProductMediaStorage? _mediaStorage;

    public ValidateProductImportCommandHandler(
        IApplicationDbContext context,
        ICurrentUserService currentUserService,
        IProductImportWorkbookService workbookService,
        IProductMediaStorage? mediaStorage = null)
    {
        _context = context;
        _currentUserService = currentUserService;
        _workbookService = workbookService;
        _mediaStorage = mediaStorage;
    }

    public Task<Result<ProductImportValidationResultDto>> Handle(
        ValidateProductImportCommand request,
        CancellationToken cancellationToken)
    {
        return ExecuteCoreAsync(request, isInspectMode: false, cancellationToken);
    }

    internal async Task<Result<ProductImportValidationResultDto>> ExecuteCoreAsync(
        ValidateProductImportCommand request,
        bool isInspectMode,
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

        if (request.FileLength > MaxFileSizeBytes)
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

        // 2b. Parse and pre-validate imageMappings JSON if provided
        List<ProductImportImageMappingDto>? rawMappings = null;
        var mappedSkus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!isInspectMode && !string.IsNullOrWhiteSpace(request.ImageMappingsJson))
        {
            var jsonByteCount = Encoding.UTF8.GetByteCount(request.ImageMappingsJson);
            if (jsonByteCount > MaxImageMappingsJsonBytes)
            {
                return Result.Failure<ProductImportValidationResultDto>(Error.Validation(
                    "ProductImport.ImageMappingsTooLarge",
                    "Dữ liệu imageMappings vượt quá giới hạn 512 KiB."));
            }

            var trimmedJson = request.ImageMappingsJson.Trim();
            if (!trimmedJson.StartsWith('[') || !trimmedJson.EndsWith(']'))
            {
                return Result.Failure<ProductImportValidationResultDto>(Error.Validation(
                    "ProductImport.InvalidImageMappingsJson",
                    "Dữ liệu imageMappings phải là một mảng JSON hợp lệ."));
            }

            List<ProductImportImageMappingDto?>? deserialized;
            try
            {
                deserialized = JsonSerializer.Deserialize<List<ProductImportImageMappingDto?>>(trimmedJson, JsonOptions);
            }
            catch (JsonException)
            {
                return Result.Failure<ProductImportValidationResultDto>(Error.Validation(
                    "ProductImport.InvalidImageMappingsJson",
                    "Dữ liệu imageMappings không đúng định dạng JSON hợp lệ."));
            }

            if (deserialized == null || deserialized.Any(x => x is null))
            {
                return Result.Failure<ProductImportValidationResultDto>(Error.Validation(
                    "ProductImport.InvalidImageMappingsJson",
                    "Dữ liệu imageMappings không được chứa phần tử rỗng (null)."));
            }

            rawMappings = deserialized.Select(x => x!).ToList();

            if (rawMappings.Count > MaxImageMappingsCount)
            {
                return Result.Failure<ProductImportValidationResultDto>(Error.Validation(
                    "ProductImport.TooManyImageMappings",
                    $"Số lượng SKU trong imageMappings không được vượt quá {MaxImageMappingsCount}."));
            }

            var totalKeysCount = rawMappings.Sum(m =>
                (string.IsNullOrWhiteSpace(m.PrimaryObjectKey) ? 0 : 1) +
                (m.AdditionalObjectKeys?.Count ?? 0));

            if (totalKeysCount > MaxTotalObjectKeysCount)
            {
                return Result.Failure<ProductImportValidationResultDto>(Error.Validation(
                    "ProductImport.TooManyObjectKeys",
                    $"Tổng số lượng object key trong imageMappings không được vượt quá {MaxTotalObjectKeysCount}."));
            }

            if (rawMappings.Count > 0)
            {
                if (_mediaStorage == null || !_mediaStorage.IsEnabled)
                {
                    return Result.Failure<ProductImportValidationResultDto>(Error.Unavailable(
                        "ProductMedia.StorageDisabled",
                        "Dịch vụ lưu trữ ảnh sản phẩm chưa được cấu hình hoặc đang tạm ngưng."));
                }

                foreach (var m in rawMappings)
                {
                    if (!string.IsNullOrWhiteSpace(m.Sku))
                    {
                        mappedSkus.Add(m.Sku.Trim().ToUpperInvariant());
                    }
                }
            }
        }

        // 3. Parse workbook and initial schema / cell / cross-sheet linking checks
        Result<ProductImportParsedWorkbook> parseResult;
        if (!isInspectMode && (rawMappings == null || rawMappings.Count == 0))
        {
            parseResult = await _workbookService.ParseWorkbookAsync(request.FileStream, cancellationToken);
        }
        else
        {
            var parseOptions = new ProductImportWorkbookParseOptions(
                AllowMissingImageUrl: isInspectMode,
                SkusWithDeferredImageValidation: mappedSkus.Count > 0 ? mappedSkus : null);
            parseResult = await _workbookService.ParseWorkbookAsync(request.FileStream, parseOptions, cancellationToken);
        }

        if (!parseResult.IsSuccess)
        {
            return Result.Failure<ProductImportValidationResultDto>(parseResult.Error);
        }

        var parsed = parseResult.Value;
        var allErrors = new List<ProductImportErrorDto>(parsed.ParseErrors);
        var invalidProductRows = new HashSet<int>();

        // Map SKU to product row numbers (both from parsed rows and any Products sheet parse errors)
        var skuToProductRows = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in parsed.Rows)
        {
            if (!string.IsNullOrWhiteSpace(r.Sku))
            {
                var key = r.Sku.Trim().ToUpperInvariant();
                if (!skuToProductRows.TryGetValue(key, out var list))
                {
                    list = [];
                    skuToProductRows[key] = list;
                }
                if (!list.Contains(r.RowNumber))
                {
                    list.Add(r.RowNumber);
                }
            }
        }

        foreach (var err in parsed.ParseErrors)
        {
            if (string.Equals(err.Sheet, "Products", StringComparison.OrdinalIgnoreCase) &&
                err.Row >= 2 &&
                !string.IsNullOrWhiteSpace(err.Sku))
            {
                var key = err.Sku.Trim().ToUpperInvariant();
                if (!skuToProductRows.TryGetValue(key, out var list))
                {
                    list = [];
                    skuToProductRows[key] = list;
                }
                if (!list.Contains(err.Row))
                {
                    list.Add(err.Row);
                }
            }
        }

        foreach (var error in parsed.ParseErrors)
        {
            if (string.Equals(error.Sheet, "Products", StringComparison.OrdinalIgnoreCase) && error.Row >= 2)
            {
                invalidProductRows.Add(error.Row);
            }
            else if (!string.IsNullOrWhiteSpace(error.Sku) &&
                     skuToProductRows.TryGetValue(error.Sku.Trim().ToUpperInvariant(), out var pRows))
            {
                foreach (var pRow in pRows)
                {
                    invalidProductRows.Add(pRow);
                }
            }
        }

        // 3b. Validate and resolve imageMappings against workbook SKUs and R2 storage metadata
        var skusWithMappingError = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (rawMappings is { Count: > 0 } && _mediaStorage != null)
        {
            var mappingResolution = await ValidateAndResolveImageMappingsAsync(
                rawMappings,
                skuToProductRows,
                allErrors,
                invalidProductRows,
                skusWithMappingError,
                cancellationToken);

            if (mappingResolution.IsFailure)
            {
                return Result.Failure<ProductImportValidationResultDto>(mappingResolution.Error);
            }

            var resolvedBySku = mappingResolution.Value;
            if (resolvedBySku.Count > 0)
            {
                var updatedRows = parsed.Rows
                    .Select(row =>
                    {
                        var normSku = row.Sku?.Trim().ToUpperInvariant() ?? string.Empty;
                        if (!string.IsNullOrEmpty(normSku) &&
                            resolvedBySku.TryGetValue(normSku, out var resolved))
                        {
                            return row with
                            {
                                ImageUrl = resolved.PrimaryImageUrl,
                                AdditionalImages = resolved.AdditionalImageUrls
                            };
                        }
                        return row;
                    })
                    .ToList();

                parsed = parsed with { Rows = updatedRows };
            }
        }

        request.ResolvedWorkbook = parsed;

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
            var normRowSku = row.Sku?.Trim().ToUpperInvariant() ?? string.Empty;
            if (!string.IsNullOrEmpty(normRowSku) && dbSkuSet.Contains(normRowSku))
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
            var fakeCategoryId = category?.Id ?? Guid.NewGuid();
            var commandDraft = new CreateProductCommand(
                fakeCategoryId,
                row.Name ?? string.Empty,
                row.Sku ?? string.Empty,
                row.Brand ?? string.Empty,
                row.Price,
                row.OriginalPrice,
                row.StockQuantity,
                row.ImageUrl ?? string.Empty,
                row.AdditionalImages,
                row.ComponentType,
                row.Specification!);

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

                    if (row.Specification == null && failure.PropertyName == "Specifications")
                    {
                        // Missing specification already reported by LinkProductsWithSpecifications
                        continue;
                    }

                    if (failure.PropertyName == "ImageUrl")
                    {
                        if (isInspectMode && string.IsNullOrWhiteSpace(row.ImageUrl))
                        {
                            continue;
                        }

                        if (!string.IsNullOrEmpty(normRowSku) &&
                            skusWithMappingError.Contains(normRowSku) &&
                            string.IsNullOrWhiteSpace(row.ImageUrl))
                        {
                            continue;
                        }
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
                    else if (failure.PropertyName.StartsWith("AdditionalImages", StringComparison.OrdinalIgnoreCase))
                    {
                        sheet = "Products";
                        rowNum = row.RowNumber;
                        col = "AdditionalImages";
                        code = "ProductImport.InvalidAdditionalImages";
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

    private async Task<Result<Dictionary<string, ProductImportResolvedImageMapping>>> ValidateAndResolveImageMappingsAsync(
        List<ProductImportImageMappingDto> mappings,
        Dictionary<string, List<int>> skuToProductRows,
        List<ProductImportErrorDto> allErrors,
        HashSet<int> invalidProductRows,
        HashSet<string> skusWithMappingError,
        CancellationToken cancellationToken)
    {
        var seenSkus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicateMappedSkus = mappings
            .Where(m => !string.IsNullOrWhiteSpace(m.Sku))
            .GroupBy(m => m.Sku!.Trim().ToUpperInvariant(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var candidateMappings = new List<(string NormSku, string RawSku, int TargetRow, List<int> AllRows, string PrimaryKey, List<string> AdditionalKeys)>();
        var distinctKeysToFetch = new HashSet<string>(StringComparer.Ordinal);

        void MarkSkuError(string normSku, List<int>? rows)
        {
            if (!string.IsNullOrEmpty(normSku))
            {
                skusWithMappingError.Add(normSku);
            }
            if (rows != null)
            {
                foreach (var r in rows)
                {
                    if (r >= 2) invalidProductRows.Add(r);
                }
            }
        }

        foreach (var mapping in mappings)
        {
            var rawSku = mapping.Sku?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(rawSku))
            {
                allErrors.Add(new ProductImportErrorDto(
                    "Products",
                    0,
                    "SKU",
                    string.Empty,
                    "ProductImport.InvalidImageMappingSku",
                    "Mã SKU trong imageMappings không được để trống."));
                continue;
            }

            var normSku = rawSku.ToUpperInvariant();
            skuToProductRows.TryGetValue(normSku, out var prodRows);
            var targetRow = prodRows is { Count: > 0 } ? prodRows[0] : 0;

            if (!seenSkus.Add(normSku) || duplicateMappedSkus.Contains(normSku))
            {
                if (seenSkus.Contains(normSku) && duplicateMappedSkus.Contains(normSku))
                {
                    allErrors.Add(new ProductImportErrorDto(
                        "Products",
                        targetRow,
                        "ImageUrl",
                        rawSku,
                        "ProductImport.DuplicateImageMappingSku",
                        $"Mã SKU '{rawSku}' bị lặp lại trong danh sách imageMappings."));
                }
                MarkSkuError(normSku, prodRows);
                continue;
            }

            if (prodRows == null || prodRows.Count == 0)
            {
                allErrors.Add(new ProductImportErrorDto(
                    "Products",
                    0,
                    "SKU",
                    rawSku,
                    "ProductImport.UnknownImageMappingSku",
                    $"Mã SKU '{rawSku}' trong imageMappings không tồn tại trong sheet 'Products'."));
                continue;
            }

            var hasSkuError = false;
            var primaryKey = mapping.PrimaryObjectKey?.Trim() ?? string.Empty;
            var seenKeysInSku = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(primaryKey))
            {
                allErrors.Add(new ProductImportErrorDto(
                    "Products",
                    targetRow,
                    "ImageUrl",
                    rawSku,
                    "ProductImport.MissingPrimaryObjectKey",
                    $"SKU '{rawSku}' trong imageMappings thiếu primaryObjectKey."));
                hasSkuError = true;
            }
            else if (!ProductMediaHelper.IsValidManagedObjectKey(primaryKey))
            {
                allErrors.Add(new ProductImportErrorDto(
                    "Products",
                    targetRow,
                    "ImageUrl",
                    rawSku,
                    "ProductImport.InvalidObjectKey",
                    $"ObjectKey ảnh chính '{primaryKey}' của SKU '{rawSku}' không hợp lệ hoặc nằm ngoài thư mục quản lý."));
                hasSkuError = true;
            }
            else
            {
                seenKeysInSku.Add(primaryKey);
            }

            var cleanAdditionalKeys = new List<string>();
            if (mapping.AdditionalObjectKeys != null)
            {
                if (mapping.AdditionalObjectKeys.Count > MaxAdditionalImagesPerProduct)
                {
                    allErrors.Add(new ProductImportErrorDto(
                        "Products",
                        targetRow,
                        "AdditionalImages",
                        rawSku,
                        "ProductImport.TooManyAdditionalImages",
                        $"SKU '{rawSku}' có vượt quá {MaxAdditionalImagesPerProduct} ảnh phụ trong imageMappings."));
                    hasSkuError = true;
                }

                foreach (var rawAddKey in mapping.AdditionalObjectKeys)
                {
                    var addKey = rawAddKey?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(addKey) || !ProductMediaHelper.IsValidManagedObjectKey(addKey))
                    {
                        allErrors.Add(new ProductImportErrorDto(
                            "Products",
                            targetRow,
                            "AdditionalImages",
                            rawSku,
                            "ProductImport.InvalidObjectKey",
                            $"ObjectKey ảnh phụ '{addKey}' của SKU '{rawSku}' không hợp lệ hoặc nằm ngoài thư mục quản lý."));
                        hasSkuError = true;
                    }
                    else if (!seenKeysInSku.Add(addKey))
                    {
                        allErrors.Add(new ProductImportErrorDto(
                            "Products",
                            targetRow,
                            "AdditionalImages",
                            rawSku,
                            "ProductImport.DuplicateObjectKey",
                            $"ObjectKey ảnh phụ '{addKey}' của SKU '{rawSku}' bị trùng với ảnh chính hoặc ảnh phụ khác."));
                        hasSkuError = true;
                    }
                    else
                    {
                        cleanAdditionalKeys.Add(addKey);
                    }
                }
            }

            if (hasSkuError)
            {
                MarkSkuError(normSku, prodRows);
                continue;
            }

            distinctKeysToFetch.Add(primaryKey);
            foreach (var addKey in cleanAdditionalKeys)
            {
                distinctKeysToFetch.Add(addKey);
            }

            candidateMappings.Add((normSku, rawSku, targetRow, prodRows, primaryKey, cleanAdditionalKeys));
        }

        var resolvedDict = new Dictionary<string, ProductImportResolvedImageMapping>(StringComparer.OrdinalIgnoreCase);
        if (candidateMappings.Count == 0)
        {
            return Result.Success(resolvedDict);
        }

        // Fetch R2 object metadata with per-request cache and bounded concurrency (max 3)
        var metadataCache = new ConcurrentDictionary<string, Result<ProductMediaMetadata>>(StringComparer.Ordinal);
        using var semaphore = new SemaphoreSlim(MaxMetadataHeadConcurrency, MaxMetadataHeadConcurrency);

        var fetchTasks = distinctKeysToFetch.Select(async key =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var metaResult = await _mediaStorage!.GetObjectMetadataAsync(key, cancellationToken);
                metadataCache[key] = metaResult;
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(fetchTasks);

        // If any storage check failed due to Unavailable (503), abort immediately with 503
        foreach (var kvp in metadataCache)
        {
            if (kvp.Value.IsFailure && kvp.Value.Error.Type == ErrorType.Unavailable)
            {
                return Result.Failure<Dictionary<string, ProductImportResolvedImageMapping>>(kvp.Value.Error);
            }
        }

        var maxBytes = _mediaStorage!.MaxFileSizeBytes > 0
            ? _mediaStorage.MaxFileSizeBytes
            : 5 * 1024 * 1024;

        foreach (var candidate in candidateMappings)
        {
            var hasMetaError = false;

            if (!ValidateKeyMetadata(
                    candidate.PrimaryKey,
                    "ImageUrl",
                    candidate.RawSku,
                    candidate.TargetRow,
                    maxBytes,
                    metadataCache,
                    allErrors))
            {
                hasMetaError = true;
            }

            foreach (var addKey in candidate.AdditionalKeys)
            {
                if (!ValidateKeyMetadata(
                        addKey,
                        "AdditionalImages",
                        candidate.RawSku,
                        candidate.TargetRow,
                        maxBytes,
                        metadataCache,
                        allErrors))
                {
                    hasMetaError = true;
                }
            }

            if (hasMetaError)
            {
                MarkSkuError(candidate.NormSku, candidate.AllRows);
                continue;
            }

            var primaryPublicUrl = _mediaStorage.GetPublicUrl(candidate.PrimaryKey);
            var additionalPublicUrls = candidate.AdditionalKeys
                .Select(k => _mediaStorage.GetPublicUrl(k))
                .ToList();

            resolvedDict[candidate.NormSku] = new ProductImportResolvedImageMapping(
                primaryPublicUrl,
                additionalPublicUrls);
        }

        return Result.Success(resolvedDict);
    }

    private static bool ValidateKeyMetadata(
        string objectKey,
        string columnName,
        string rawSku,
        int targetRow,
        long maxBytes,
        ConcurrentDictionary<string, Result<ProductMediaMetadata>> metadataCache,
        List<ProductImportErrorDto> allErrors)
    {
        if (!metadataCache.TryGetValue(objectKey, out var metaResult) || metaResult.IsFailure)
        {
            allErrors.Add(new ProductImportErrorDto(
                "Products",
                targetRow,
                columnName,
                rawSku,
                "ProductImport.MediaNotFound",
                $"Không tìm thấy file ảnh '{objectKey}' của SKU '{rawSku}' trên kho lưu trữ."));
            return false;
        }

        var meta = metaResult.Value;
        var actualMime = meta.ContentType?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(actualMime) || !ProductMediaHelper.IsSupportedMimeType(actualMime))
        {
            allErrors.Add(new ProductImportErrorDto(
                "Products",
                targetRow,
                columnName,
                rawSku,
                "ProductImport.InvalidMediaMetadata",
                $"File ảnh '{objectKey}' của SKU '{rawSku}' có định dạng MIME '{meta.ContentType}' không được hỗ trợ."));
            return false;
        }

        var extension = Path.GetExtension(objectKey);
        var expectedMime = ProductMediaHelper.GetExpectedMimeTypeForExtension(extension);
        if (!string.Equals(actualMime, expectedMime, StringComparison.OrdinalIgnoreCase))
        {
            allErrors.Add(new ProductImportErrorDto(
                "Products",
                targetRow,
                columnName,
                rawSku,
                "ProductImport.InvalidMediaMetadata",
                $"Định dạng MIME '{actualMime}' của file '{objectKey}' (SKU '{rawSku}') không khớp với phần mở rộng '{extension}'."));
            return false;
        }

        if (meta.ContentLength <= 0 || meta.ContentLength > maxBytes)
        {
            allErrors.Add(new ProductImportErrorDto(
                "Products",
                targetRow,
                columnName,
                rawSku,
                "ProductImport.InvalidMediaMetadata",
                $"Dung lượng file ảnh '{objectKey}' của SKU '{rawSku}' ({meta.ContentLength} bytes) không hợp lệ (tối đa {maxBytes} bytes)."));
            return false;
        }

        return true;
    }
}

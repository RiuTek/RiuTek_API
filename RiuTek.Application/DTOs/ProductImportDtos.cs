using System.Text.Json.Serialization;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;

namespace RiuTek.Application.DTOs;

public record ProductImportValidationResultDto(
    string TemplateVersion,
    bool IsValid,
    int TotalRows,
    int ValidRows,
    int InvalidRows,
    bool ErrorsTruncated,
    IReadOnlyList<ProductImportErrorDto> Errors);

public record ProductImportErrorDto(
    string Sheet,
    int Row,
    string? Column,
    string? Sku,
    string Code,
    string Message);

public record ProductImportInspectRowDto(
    string Sheet,
    int Row,
    string Sku,
    string Name,
    [property: JsonConverter(typeof(JsonStringEnumConverter))]
    ComponentType ComponentType,
    string? PrimaryImageFile,
    string? ImageUrl);

public record ProductImportInspectResultDto(
    string TemplateVersion,
    bool IsValid,
    IReadOnlyList<ProductImportInspectRowDto> Rows,
    IReadOnlyList<ProductImportErrorDto> Errors,
    bool ErrorsTruncated);

public record ProductImportImageMappingDto(
    string? Sku,
    string? PrimaryObjectKey,
    IReadOnlyList<string?>? AdditionalObjectKeys = null);

public record ProductImportResolvedImageMapping(
    string PrimaryImageUrl,
    IReadOnlyList<string> AdditionalImageUrls);

public record ProductImportWorkbookParseOptions(
    bool AllowMissingImageUrl = false,
    IReadOnlyDictionary<string, ProductImportResolvedImageMapping>? ImageOverrides = null,
    IReadOnlySet<string>? SkusWithDeferredImageValidation = null);

public record ProductImportTemplateDto(
    byte[] Content,
    string ContentType,
    string FileName);

public record CategoryLookupDto(
    string Slug,
    string Name,
    ComponentType ComponentType);

public record ProductImportRowDraft(
    int RowNumber,
    string Sku,
    string Name,
    string CategorySlug,
    ComponentType ComponentType,
    string Brand,
    decimal Price,
    decimal? OriginalPrice,
    int StockQuantity,
    string? PrimaryImageFile,
    string ImageUrl,
    ComponentSpecification? Specification,
    int? SpecRowNumber,
    string? SpecSheetName,
    IReadOnlyList<string>? AdditionalImages = null);

public record ProductImportParsedWorkbook(
    string TemplateVersion,
    int TotalProductRows,
    IReadOnlyList<ProductImportRowDraft> Rows,
    IReadOnlyList<ProductImportErrorDto> ParseErrors);

public record ProductImportCreatedProductDto(
    Guid Id,
    string Sku,
    string Slug);

public record ProductImportCommitResultDto(
    string TemplateVersion,
    bool IsCommitted,
    int CreatedCount,
    IReadOnlyList<ProductImportCreatedProductDto> CreatedProducts,
    ProductImportValidationResultDto Validation);

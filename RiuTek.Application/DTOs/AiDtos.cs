namespace RiuTek.Application.DTOs;

public record ChatMessageDto(
    string Role,
    string Content);

public record AiProductDto(
    string Sku,
    string Name,
    string Slug,
    string Url,
    decimal PriceVnd,
    bool InStock,
    bool IsActive,
    string? ImageUrl);

public record AiSourceDto(
    string Kind,
    string Title,
    string? Url,
    IReadOnlyList<string> FactKeys);

public record AiConsultantResponseDto(
    string Status,
    string Answer,
    IReadOnlyList<AiProductDto> Products,
    IReadOnlyList<AiSourceDto> Sources,
    AiBuildDto? Build = null);

public record AiBuildDto(
    decimal BudgetVnd,
    decimal TotalVnd,
    int RecommendedPsuWattage,
    IReadOnlyList<AiBuildItemDto> Items,
    string? UseCase = null,
    int MinRamGb = 0,
    int MinStorageGb = 0,
    IReadOnlyList<string>? KeptComponents = null);

public record AiPreviousBuildDto(
    decimal BudgetVnd,
    string UseCase,
    IReadOnlyList<AiPreviousBuildItemDto> Items,
    int MinRamGb = 0,
    int MinStorageGb = 0,
    IReadOnlyList<string>? KeptComponents = null);

public record AiPreviousBuildItemDto(Guid ProductId, bool AlreadyOwned);

public record AiBuildItemDto(Guid ProductId, AiProductDto Product, string Component, string Reason, bool AlreadyOwned);

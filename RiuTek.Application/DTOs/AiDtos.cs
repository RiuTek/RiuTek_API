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
    bool IsActive);

public record AiSourceDto(
    string Kind,
    string Title,
    string? Url,
    IReadOnlyList<string> FactKeys);

public record AiConsultantResponseDto(
    string Status,
    string Answer,
    IReadOnlyList<AiProductDto> Products,
    IReadOnlyList<AiSourceDto> Sources);

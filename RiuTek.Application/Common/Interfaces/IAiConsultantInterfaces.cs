using RiuTek.Application.DTOs;
using RiuTek.Core.Common;

namespace RiuTek.Application.Common.Interfaces;

public interface IAiRuntimeSettings
{
    bool Enabled { get; }
    bool AllowAnonymous { get; }
    bool WebSearchEnabled { get; }
    bool IsConfigured { get; }
}

public record QuotaReservationResult(
    bool IsAllowed,
    bool IsRedisUnavailable = false,
    string? ErrorReason = null,
    int? RetryAfterSeconds = null);

public interface IAiQuotaService
{
    Task<QuotaReservationResult> ReserveTurnAsync(string clientKey, CancellationToken cancellationToken = default);
    Task<QuotaReservationResult> ReserveCustomerTurnAsync(CancellationToken cancellationToken = default);
    Task<QuotaReservationResult> ReserveProviderRequestAsync(CancellationToken cancellationToken = default);
    Task<QuotaReservationResult> ReserveWebRequestAsync(CancellationToken cancellationToken = default);
}

public record GroqStructuredPlan(
    string TaskType,
    IReadOnlyList<string> TargetProducts,
    string? ComponentType = null,
    decimal? MinPrice = null,
    decimal? MaxPrice = null,
    bool? InStockOnly = null,
    IReadOnlyDictionary<string, string>? TechnicalCriteria = null,
    IReadOnlyList<string>? RequiredFacts = null,
    string? ClarificationQuestion = null,
    int? MinPsuWattage = null,
    int? ExactPsuWattage = null,
    string? BuildUseCase = null,
    IReadOnlyList<string>? OwnedProducts = null,
    string? BuildAction = null,
    IReadOnlyList<string>? KeepComponents = null,
    string? UpgradeComponent = null);

public record GroqWebSearchResult(
    string FactKey,
    string Summary,
    string SourceTitle,
    string SourceUrl,
    string? ProductKey = null,
    string? ModelName = null);

public record FaqEntry(
    string Key,
    string Title,
    string Content,
    IReadOnlyList<string> Keywords);

public record GroqSynthesisRequest(
    string UserQuestion,
    string TaskType,
    IReadOnlyList<ProductDto> CatalogProducts,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ProductFactsBySku,
    IReadOnlyList<GroqWebSearchResult> WebFacts,
    IReadOnlyList<FaqEntry> FaqEntries,
    IReadOnlyList<ChatMessageDto>? History = null,
    bool HasMissingTechnicalFacts = false);

public record GroqSynthesisResult(
    string Status,
    string Answer,
    IReadOnlyList<string> SupportingFactKeys);

public interface IGroqClient
{
    Task<Result<GroqStructuredPlan>> AnalyzeQuestionAsync(
        string question,
        string? productSlug,
        IReadOnlyList<ChatMessageDto>? history,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<GroqWebSearchResult>>> SearchWebForTechnicalFactsAsync(
        string brandAndModel,
        IReadOnlyList<string> missingFacts,
        CancellationToken cancellationToken = default);

    Task<Result<GroqSynthesisResult>> SynthesizeAnswerAsync(
        GroqSynthesisRequest request,
        CancellationToken cancellationToken = default);
}

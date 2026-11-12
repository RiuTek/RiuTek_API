using System.Globalization;
using System.Diagnostics;
using System.Text.RegularExpressions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Common.Mappings;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Ai.Common;
using RiuTek.Application.Features.Products.Queries;
using RiuTek.Core.Common;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Features.Ai.Queries;

public record AskAiConsultantQuery(
    string Question,
    string? ProductSlug,
    IReadOnlyList<ChatMessageDto>? History,
    string ClientQuotaKey) : IRequest<Result<AiConsultantResponseDto>>;

public class AskAiConsultantQueryValidator : AbstractValidator<AskAiConsultantQuery>
{
    private static readonly HashSet<string> AllowedRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "user",
        "assistant"
    };

    public AskAiConsultantQueryValidator()
    {
        RuleFor(x => x.Question)
            .NotEmpty()
            .WithMessage("Câu hỏi không được để trống.")
            .Must(q => q != null && q.Trim().Length is >= 1 and <= 500)
            .WithMessage("Câu hỏi sau khi cắt khoảng trắng phải từ 1 đến 500 ký tự.");

        When(x => x.ProductSlug != null, () =>
        {
            RuleFor(x => x.ProductSlug!)
                .Must(s => s.Trim().Length <= 200)
                .WithMessage("ProductSlug không được vượt quá 200 ký tự.");
        });

        When(x => x.History != null, () =>
        {
            RuleFor(x => x.History!)
                .Must(h => h.Count <= 4)
                .WithMessage("Lịch sử hội thoại tối đa 4 tin nhắn.");

            RuleForEach(x => x.History!).ChildRules(msg =>
            {
                msg.RuleFor(m => m.Role)
                    .NotEmpty()
                    .WithMessage("Role trong lịch sử không được để trống.")
                    .Must(r => r != null && AllowedRoles.Contains(r.Trim()))
                    .WithMessage("Role trong lịch sử chỉ chấp nhận 'user' hoặc 'assistant'.");

                msg.RuleFor(m => m.Content)
                    .NotEmpty()
                    .WithMessage("Nội dung tin nhắn lịch sử không được để trống.")
                    .Must(c => c != null && c.Trim().Length is >= 1 and <= 500)
                    .WithMessage("Nội dung mỗi tin nhắn lịch sử phải từ 1 đến 500 ký tự.");
            });
        });
    }
}

public class AskAiConsultantQueryHandler : IRequestHandler<AskAiConsultantQuery, Result<AiConsultantResponseDto>>
{
    private sealed record ProductMissingFactsTarget(
        string ProductKey,
        string BrandAndModel,
        IReadOnlyList<string> MissingFacts);

    private static readonly HashSet<string> ChipsetVendors = new(StringComparer.OrdinalIgnoreCase)
    {
        "amd", "nvidia", "geforce", "radeon", "intel", "arc", "rtx", "gtx", "rx"
    };

    private static readonly HashSet<string> ValidStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "answered", "partial", "needs_clarification", "insufficient_data", "out_of_scope"
    };

    private readonly ISender _mediator;
    private readonly IApplicationDbContext _context;
    private readonly IAiRuntimeSettings _settings;
    private readonly IAiQuotaService _quotaService;
    private readonly IGroqClient _groqClient;
    private readonly ILogger<AskAiConsultantQueryHandler> _logger;

    public AskAiConsultantQueryHandler(
        ISender mediator,
        IApplicationDbContext context,
        IAiRuntimeSettings settings,
        IAiQuotaService quotaService,
        IGroqClient groqClient,
        ILogger<AskAiConsultantQueryHandler> logger)
    {
        _mediator = mediator;
        _context = context;
        _settings = settings;
        _quotaService = quotaService;
        _groqClient = groqClient;
        _logger = logger;
    }

    public async Task<Result<AiConsultantResponseDto>> Handle(
        AskAiConsultantQuery request,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var requestId = Guid.NewGuid().ToString("N")[..12];
        var analysisCalls = 0;
        var webCalls = 0;
        var synthesisCalls = 0;

        try
        {
            if (!_settings.Enabled)
            {
                return Result.Failure<AiConsultantResponseDto>(
                    Error.Unavailable("Ai.Disabled", "Tính năng tư vấn AI hiện đang tắt."));
            }

            if (!_settings.IsConfigured)
            {
                return Result.Failure<AiConsultantResponseDto>(
                    Error.Unavailable("Ai.NotConfigured", "Dịch vụ AI chưa được cấu hình đầy đủ."));
            }

            var question = request.Question.Trim();
            var productSlug = string.IsNullOrWhiteSpace(request.ProductSlug) ? null : request.ProductSlug.Trim();
            var sanitizedHistory = request.History?
                .Select(h => new ChatMessageDto(h.Role.Trim().ToLowerInvariant(), h.Content.Trim()))
                .ToList();

            // 1. Reserve user/session turn quota
            var turnQuota = await _quotaService.ReserveTurnAsync(request.ClientQuotaKey, cancellationToken);
            if (turnQuota.IsRedisUnavailable)
            {
                return Result.Failure<AiConsultantResponseDto>(
                    Error.Unavailable("Ai.QuotaUnavailable", "Hệ thống kiểm soát hạn mức AI tạm thời gián đoạn."));
            }

            if (!turnQuota.IsAllowed)
            {
                return Result.Failure<AiConsultantResponseDto>(
                    Error.TooManyRequests(
                        "Ai.TurnLimitExceeded",
                        "Bạn đã sử dụng hết số lượt hỏi AI trong 15 phút. Vui lòng thử lại sau.",
                        turnQuota.RetryAfterSeconds));
            }

            // 2. Reserve provider quota and analyze question
            var analyzeQuota = await _quotaService.ReserveProviderRequestAsync(cancellationToken);
            if (analyzeQuota.IsRedisUnavailable)
            {
                return Result.Failure<AiConsultantResponseDto>(
                    Error.Unavailable("Ai.QuotaUnavailable", "Hệ thống kiểm soát hạn mức AI tạm thời gián đoạn."));
            }

            if (!analyzeQuota.IsAllowed)
            {
                return Result.Failure<AiConsultantResponseDto>(
                    Error.TooManyRequests(
                        "Ai.ProviderLimitExceeded",
                        "Hệ thống AI đã đạt giới hạn phục vụ trong ngày. Vui lòng thử lại sau.",
                        analyzeQuota.RetryAfterSeconds));
            }

            analysisCalls++;
            var planResult = await _groqClient.AnalyzeQuestionAsync(question, productSlug, sanitizedHistory, cancellationToken);
            if (planResult.IsFailure)
            {
                LogMetadata(requestId, analysisCalls, webCalls, synthesisCalls, sw.ElapsedMilliseconds, "provider_error");
                return Result.Failure<AiConsultantResponseDto>(
                    Error.Unavailable("Ai.ProviderError", "Dịch vụ AI tạm thời không phản hồi. Vui lòng thử lại sau."));
            }

            var plan = planResult.Value;
            var taskType = NormalizeTaskType(plan.TaskType);

            if (taskType == "out_of_scope")
            {
                var outOfScopeResponse = new AiConsultantResponseDto(
                    Status: "out_of_scope",
                    Answer: "Xin lỗi, trợ lý RiuTek chỉ hỗ trợ tư vấn linh kiện máy tính, thông số kỹ thuật PC và chính sách mua hàng tại RiuTek.",
                    Products: [],
                    Sources: []);
                LogMetadata(requestId, analysisCalls, webCalls, synthesisCalls, sw.ElapsedMilliseconds, outOfScopeResponse.Status);
                return Result.Success(outOfScopeResponse);
            }

            if (taskType == "clarify")
            {
                var clarifyResponse = new AiConsultantResponseDto(
                    Status: "needs_clarification",
                    Answer: !string.IsNullOrWhiteSpace(plan.ClarificationQuestion)
                        ? plan.ClarificationQuestion.Trim()
                        : "Bạn vui lòng cho biết cụ thể tên hoặc mã sản phẩm linh kiện cần tư vấn để RiuTek kiểm tra chính xác nhé.",
                    Products: [],
                    Sources: []);
                LogMetadata(requestId, analysisCalls, webCalls, synthesisCalls, sw.ElapsedMilliseconds, clarifyResponse.Status);
                return Result.Success(clarifyResponse);
            }

            // 3. Resolve catalog products & FAQ / unpublished policy intent
            var matchedFaqs = CuratedFaqRepository.FindMatching(question, plan.RequiredFacts);
            var unpublishedPolicyKeys = CuratedFaqRepository.DetectUnpublishedPolicyKeys(question, plan.RequiredFacts);
            var hasNoProductTargets = (plan.TargetProducts == null || plan.TargetProducts.Count == 0) &&
                                      string.IsNullOrWhiteSpace(productSlug);

            if ((taskType == "faq" || unpublishedPolicyKeys.Count > 0) &&
                taskType != "recommend" &&
                hasNoProductTargets)
            {
                if (matchedFaqs.Count == 0)
                {
                    var noFaqResponse = new AiConsultantResponseDto(
                        Status: "insufficient_data",
                        Answer: CuratedFaqRepository.BuildStandaloneUnpublishedPolicyAnswer(),
                        Products: [],
                        Sources: []);
                    LogMetadata(requestId, analysisCalls, webCalls, synthesisCalls, sw.ElapsedMilliseconds, noFaqResponse.Status);
                    return Result.Success(noFaqResponse);
                }

                var faqAnswer = string.Join(" ", matchedFaqs.Select(f => f.Content));
                var faqSources = matchedFaqs
                    .Select(f => new AiSourceDto("faq", f.Title, null, [f.Key]))
                    .ToList();

                var faqResponse = new AiConsultantResponseDto(
                    Status: "answered",
                    Answer: faqAnswer,
                    Products: [],
                    Sources: faqSources);
                LogMetadata(requestId, analysisCalls, webCalls, synthesisCalls, sw.ElapsedMilliseconds, faqResponse.Status);
                return Result.Success(faqResponse);
            }

            var catalogProducts = new List<ProductDto>();
            var resolvedFromTargets = false;

            if (taskType == "recommend")
            {
                var recommendResult = await HandleRecommendationLookupAsync(plan, question, cancellationToken);
                if (recommendResult.AmbiguousResponse != null)
                {
                    LogMetadata(requestId, analysisCalls, webCalls, synthesisCalls, sw.ElapsedMilliseconds, recommendResult.AmbiguousResponse.Status);
                    return Result.Success(recommendResult.AmbiguousResponse);
                }

                catalogProducts.AddRange(recommendResult.Products);
                if (catalogProducts.Count == 0)
                {
                    var emptyRecommendResponse = new AiConsultantResponseDto(
                        Status: "answered",
                        Answer: "Hiện tại RiuTek không tìm thấy linh kiện đang mở bán khớp hoàn toàn với mức ngân sách và tiêu chí kỹ thuật bạn yêu cầu. Bạn có thể cân nhắc nới rộng khoảng giá hoặc thay đổi tiêu chí lọc để có thêm lựa chọn.",
                        Products: [],
                        Sources: [new AiSourceDto("catalog", "Danh mục sản phẩm RiuTek", null, ["priceVnd", "inStock", "isActive"])]);
                    LogMetadata(requestId, analysisCalls, webCalls, synthesisCalls, sw.ElapsedMilliseconds, emptyRecommendResponse.Status);
                    return Result.Success(emptyRecommendResponse);
                }
            }
            else
            {
                var maxTargets = taskType == "compare" ? 2 : 1;
                var targets = (plan.TargetProducts ?? [])
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Select(t => t.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(maxTargets)
                    .ToList();

                foreach (var target in targets)
                {
                    var lookup = await LookupSingleProductAsync(target, cancellationToken);
                    if (lookup.IsAmbiguous)
                    {
                        var variants = lookup.Candidates.Take(5).ToList();
                        var variantNames = string.Join(", ", variants.Select(v => $"{v.Name} ({v.Sku})"));
                        var clarifyVariants = new AiConsultantResponseDto(
                            Status: "needs_clarification",
                            Answer: $"Có nhiều phiên bản sản phẩm khớp với '{target}' trong danh mục: {variantNames}. Bạn muốn xem phiên bản nào?",
                            Products: variants.Select(MapToAiProductDto).ToList(),
                            Sources: [new AiSourceDto("catalog", "Danh mục sản phẩm RiuTek", null, ["sku", "name", "priceVnd"])]);
                        LogMetadata(requestId, analysisCalls, webCalls, synthesisCalls, sw.ElapsedMilliseconds, clarifyVariants.Status);
                        return Result.Success(clarifyVariants);
                    }

                    if (lookup.ExactOrSingleMatch != null &&
                        catalogProducts.All(p => p.Id != lookup.ExactOrSingleMatch.Id))
                    {
                        catalogProducts.Add(lookup.ExactOrSingleMatch);
                        resolvedFromTargets = true;
                    }
                }

                // Use page context productSlug when question refers to current product or compare needs second item
                if (!string.IsNullOrWhiteSpace(productSlug) &&
                    (!resolvedFromTargets || (taskType == "compare" && catalogProducts.Count < 2)))
                {
                    var slugResult = await _mediator.Send(new GetProductBySlugQuery(productSlug), cancellationToken);
                    if (slugResult.IsSuccess && catalogProducts.All(p => p.Id != slugResult.Value.Id))
                    {
                        catalogProducts.Add(slugResult.Value);
                    }
                }
            }

            // 4. Extract typed facts from catalog products
            var productFactsBySku = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            var allCatalogFactKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var prod in catalogProducts)
            {
                var extracted = ProductSpecFactExtractor.ExtractCatalogFacts(prod);
                productFactsBySku[prod.Sku] = extracted;
                foreach (var key in extracted.Keys)
                {
                    allCatalogFactKeys.Add(key);
                }
            }

            // 5. Determine missing technical facts per product (ProductKey, FactKey)
            var targetsWithMissingFacts = new List<ProductMissingFactsTarget>();

            if (catalogProducts.Count > 0)
            {
                foreach (var prod in catalogProducts)
                {
                    var missingForProd = ProductSpecFactExtractor.FindMissingTechnicalFacts(
                        plan.RequiredFacts,
                        productFactsBySku[prod.Sku]);

                    if (missingForProd.Count > 0)
                    {
                        targetsWithMissingFacts.Add(new ProductMissingFactsTarget(
                            ProductKey: prod.Sku,
                            BrandAndModel: $"{prod.Brand} {prod.Name}".Trim(),
                            MissingFacts: missingForProd));
                    }
                }
            }
            else if (plan.RequiredFacts is { Count: > 0 } &&
                     plan.TargetProducts is { Count: 1 })
            {
                var candidateModel = plan.TargetProducts[0].Trim();
                var tokens = candidateModel.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length >= 2 && candidateModel.Any(char.IsDigit))
                {
                    var missingForUnlisted = plan.RequiredFacts
                        .Where(ProductSpecFactExtractor.IsWebSearchableTechnicalFact)
                        .Select(ProductSpecFactExtractor.NormalizeFactKey)
                        .Where(f => !string.IsNullOrWhiteSpace(f))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (missingForUnlisted.Count > 0)
                    {
                        targetsWithMissingFacts.Add(new ProductMissingFactsTarget(
                            ProductKey: candidateModel,
                            BrandAndModel: candidateModel,
                            MissingFacts: missingForUnlisted));
                    }
                }
            }

            var webFacts = new List<GroqWebSearchResult>();
            var webBranchDegraded = false;

            var primaryWebTarget = (taskType is "product_info" or "compare")
                ? targetsWithMissingFacts.FirstOrDefault()
                : null;

            if (primaryWebTarget != null)
            {
                if (!_settings.WebSearchEnabled)
                {
                    webBranchDegraded = true;
                }
                else
                {
                    var webQuota = await _quotaService.ReserveWebRequestAsync(cancellationToken);
                    if (webQuota.IsRedisUnavailable)
                    {
                        if (catalogProducts.Count == 0 && matchedFaqs.Count == 0)
                        {
                            return Result.Failure<AiConsultantResponseDto>(
                                Error.Unavailable("Ai.QuotaUnavailable", "Hệ thống kiểm soát hạn mức AI tạm thời gián đoạn."));
                        }

                        webBranchDegraded = true;
                    }
                    else if (!webQuota.IsAllowed)
                    {
                        webBranchDegraded = true;
                    }
                    else
                    {
                        var webProviderQuota = await _quotaService.ReserveProviderRequestAsync(cancellationToken);
                        if (webProviderQuota.IsRedisUnavailable || !webProviderQuota.IsAllowed)
                        {
                            webBranchDegraded = true;
                        }
                        else
                        {
                            webCalls++;
                            var webResult = await _groqClient.SearchWebForTechnicalFactsAsync(
                                primaryWebTarget.BrandAndModel,
                                primaryWebTarget.MissingFacts,
                                cancellationToken);

                            if (webResult.IsSuccess && webResult.Value.Count > 0)
                            {
                                var targetMissingSet = primaryWebTarget.MissingFacts
                                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                                foreach (var rawWf in webResult.Value)
                                {
                                    if (!ProductSpecFactExtractor.IsEvidenceValidForTarget(
                                        rawWf,
                                        primaryWebTarget.BrandAndModel,
                                        primaryWebTarget.ProductKey,
                                        targetMissingSet))
                                    {
                                        continue;
                                    }

                                    var normalizedKey = ProductSpecFactExtractor.NormalizeFactKey(rawWf.FactKey);
                                    if (webFacts.Any(existing =>
                                        string.Equals(existing.ProductKey, primaryWebTarget.ProductKey, StringComparison.OrdinalIgnoreCase) &&
                                        string.Equals(existing.FactKey, normalizedKey, StringComparison.OrdinalIgnoreCase)))
                                    {
                                        continue;
                                    }

                                    webFacts.Add(rawWf with
                                    {
                                        FactKey = normalizedKey,
                                        ProductKey = primaryWebTarget.ProductKey,
                                        ModelName = primaryWebTarget.BrandAndModel
                                    });
                                }

                                if (webFacts.Count == 0)
                                {
                                    webBranchDegraded = true;
                                }
                            }
                            else
                            {
                                webBranchDegraded = true;
                            }
                        }
                    }
                }
            }

            // Compute unresolved missing technical facts per product (ProductKey, FactKey)
            var unresolvedMissingFacts = new List<string>();
            foreach (var target in targetsWithMissingFacts)
            {
                foreach (var mf in target.MissingFacts)
                {
                    var isVerifiedForTarget = webFacts.Any(wf =>
                        string.Equals(wf.ProductKey, target.ProductKey, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(wf.FactKey, mf, StringComparison.OrdinalIgnoreCase));

                    if (!isVerifiedForTarget)
                    {
                        unresolvedMissingFacts.Add($"{target.ProductKey}:{mf}");
                    }
                }
            }

            var hasUnpublishedPolicyInTurn = unpublishedPolicyKeys.Count > 0;
            var hasUnresolvedFacts = unresolvedMissingFacts.Count > 0 || webBranchDegraded || hasUnpublishedPolicyInTurn;

            // If no catalog products, no FAQ, and no verified web facts
            if (catalogProducts.Count == 0 && matchedFaqs.Count == 0 && webFacts.Count == 0)
            {
                if (hasUnpublishedPolicyInTurn && (plan.TargetProducts == null || plan.TargetProducts.Count == 0))
                {
                    var policyInsufficient = new AiConsultantResponseDto(
                        Status: "insufficient_data",
                        Answer: CuratedFaqRepository.BuildStandaloneUnpublishedPolicyAnswer(),
                        Products: [],
                        Sources: []);
                    LogMetadata(requestId, analysisCalls, webCalls, synthesisCalls, sw.ElapsedMilliseconds, policyInsufficient.Status);
                    return Result.Success(policyInsufficient);
                }

                var noDataStatus = (plan.TargetProducts == null || plan.TargetProducts.Count == 0)
                    ? "needs_clarification"
                    : "insufficient_data";

                var noDataAnswer = noDataStatus == "needs_clarification"
                    ? "Bạn vui lòng nêu rõ tên hoặc mã sản phẩm cần tra cứu để RiuTek hỗ trợ chính xác nhé."
                    : "RiuTek không tìm thấy sản phẩm hoặc dữ liệu kỹ thuật đã xác minh cho mẫu linh kiện này trong danh mục hiện tại.";

                var insufficientResponse = new AiConsultantResponseDto(
                    Status: noDataStatus,
                    Answer: noDataAnswer,
                    Products: [],
                    Sources: []);
                LogMetadata(requestId, analysisCalls, webCalls, synthesisCalls, sw.ElapsedMilliseconds, insufficientResponse.Status);
                return Result.Success(insufficientResponse);
            }

            // 6. Synthesize final response
            var synthesisRequest = new GroqSynthesisRequest(
                UserQuestion: question,
                TaskType: taskType,
                CatalogProducts: catalogProducts,
                ProductFactsBySku: productFactsBySku,
                WebFacts: webFacts,
                FaqEntries: matchedFaqs,
                History: sanitizedHistory,
                HasMissingTechnicalFacts: hasUnresolvedFacts);

            string finalStatus;
            string finalAnswer;
            var supportingKeys = new List<string>();

            var synthQuota = await _quotaService.ReserveProviderRequestAsync(cancellationToken);
            if (!synthQuota.IsRedisUnavailable && synthQuota.IsAllowed)
            {
                synthesisCalls++;
                var synthResult = await _groqClient.SynthesizeAnswerAsync(synthesisRequest, cancellationToken);
                if (synthResult.IsSuccess && !string.IsNullOrWhiteSpace(synthResult.Value.Answer))
                {
                    finalStatus = ValidStatuses.Contains(synthResult.Value.Status)
                        ? synthResult.Value.Status.ToLowerInvariant()
                        : "answered";
                    finalAnswer = synthResult.Value.Answer.Trim();
                    if (synthResult.Value.SupportingFactKeys != null)
                    {
                        supportingKeys.AddRange(synthResult.Value.SupportingFactKeys);
                    }
                }
                else if (catalogProducts.Count > 0 || matchedFaqs.Count > 0)
                {
                    finalStatus = hasUnresolvedFacts ? "partial" : "answered";
                    finalAnswer = BuildDeterministicFallbackAnswer(catalogProducts, productFactsBySku, webFacts, matchedFaqs, unresolvedMissingFacts);
                }
                else
                {
                    LogMetadata(requestId, analysisCalls, webCalls, synthesisCalls, sw.ElapsedMilliseconds, "provider_error");
                    return Result.Failure<AiConsultantResponseDto>(
                        Error.Unavailable("Ai.ProviderError", "Dịch vụ AI tạm thời không phản hồi. Vui lòng thử lại sau."));
                }
            }
            else if (catalogProducts.Count > 0 || matchedFaqs.Count > 0)
            {
                finalStatus = hasUnresolvedFacts ? "partial" : "answered";
                finalAnswer = BuildDeterministicFallbackAnswer(catalogProducts, productFactsBySku, webFacts, matchedFaqs, unresolvedMissingFacts);
            }
            else
            {
                return Result.Failure<AiConsultantResponseDto>(
                    synthQuota.IsRedisUnavailable
                        ? Error.Unavailable("Ai.QuotaUnavailable", "Hệ thống kiểm soát hạn mức AI tạm thời gián đoạn.")
                        : Error.TooManyRequests("Ai.ProviderLimitExceeded", "Hệ thống AI đã đạt giới hạn phục vụ trong ngày.", synthQuota.RetryAfterSeconds));
            }

            if (hasUnpublishedPolicyInTurn &&
                !finalAnswer.Contains("chưa được công bố", StringComparison.OrdinalIgnoreCase) &&
                !finalAnswer.Contains("chưa công bố", StringComparison.OrdinalIgnoreCase))
            {
                finalAnswer = $"{finalAnswer} {CuratedFaqRepository.BuildMixedUnpublishedPolicyNotice()}".Trim();
            }

            if (hasUnresolvedFacts && finalStatus == "answered")
            {
                finalStatus = (catalogProducts.Count > 0 || matchedFaqs.Count > 0 || webFacts.Count > 0)
                    ? "partial"
                    : "insufficient_data";
            }

            var responseProducts = catalogProducts.Select(MapToAiProductDto).ToList();
            var responseSources = BuildSources(catalogProducts, allCatalogFactKeys, supportingKeys, matchedFaqs, webFacts);

            var response = new AiConsultantResponseDto(
                Status: finalStatus,
                Answer: finalAnswer,
                Products: responseProducts,
                Sources: responseSources);

            LogMetadata(requestId, analysisCalls, webCalls, synthesisCalls, sw.ElapsedMilliseconds, response.Status);
            return Result.Success(response);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
    }

    private async Task<(ProductDto? ExactOrSingleMatch, bool IsAmbiguous, List<ProductDto> Candidates)> LookupSingleProductAsync(
        string target,
        CancellationToken cancellationToken)
    {
        var normalized = target.Trim().ToLowerInvariant();

        // 1. Exact match by SKU, Slug, or Name
        var exactMatches = await _context.Products
            .AsNoTracking()
            .Where(p => p.Sku.ToLower() == normalized ||
                        p.Slug.ToLower() == normalized ||
                        p.Name.ToLower() == normalized)
            .Select(ProductMappingExtensions.ToDtoProjection)
            .Take(5)
            .ToListAsync(cancellationToken);

        if (exactMatches.Count == 1)
        {
            return (exactMatches[0], false, exactMatches);
        }

        if (exactMatches.Count > 1)
        {
            return (null, true, exactMatches);
        }

        // 2. Fuzzy / keyword search via GetProductsQuery
        var searchFilter = new ProductFilterOptions(
            PageIndex: 1,
            PageSize: 6,
            SearchTerm: target.Length > 100 ? target[..100] : target,
            IsActive: true);

        var queryResult = await _mediator.Send(new GetProductsQuery(searchFilter), cancellationToken);
        if (queryResult.IsFailure || queryResult.Value.Items.Count == 0)
        {
            return (null, false, []);
        }

        var ids = queryResult.Value.Items.Select(i => i.Id).ToList();
        var fullCandidates = await _context.Products
            .AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(ProductMappingExtensions.ToDtoProjection)
            .ToListAsync(cancellationToken);

        if (fullCandidates.Count == 1)
        {
            return (fullCandidates[0], false, fullCandidates);
        }

        // Check if one candidate's name or SKU is an exact substring match that clearly dominates
        var exactSkuOrSlug = fullCandidates.FirstOrDefault(c =>
            string.Equals(c.Sku, target, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(c.Slug, target, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(c.Name, target, StringComparison.OrdinalIgnoreCase));

        if (exactSkuOrSlug != null)
        {
            return (exactSkuOrSlug, false, [exactSkuOrSlug]);
        }

        return (null, fullCandidates.Count > 1, fullCandidates);
    }

    private async Task<(List<ProductDto> Products, AiConsultantResponseDto? AmbiguousResponse)> HandleRecommendationLookupAsync(
        GroqStructuredPlan plan,
        string question,
        CancellationToken cancellationToken)
    {
        var filterOptions = BuildRecommendationFilterOptions(plan, question, out var invalidFilterResponse);
        if (invalidFilterResponse != null || filterOptions == null)
        {
            return ([], invalidFilterResponse);
        }

        var queryResult = await _mediator.Send(new GetProductsQuery(filterOptions), cancellationToken);
        if (queryResult.IsFailure || queryResult.Value.Items.Count == 0)
        {
            return ([], null);
        }

        var summaryIds = queryResult.Value.Items.Take(5).Select(i => i.Id).ToList();
        var products = await _context.Products
            .AsNoTracking()
            .Where(p => summaryIds.Contains(p.Id))
            .Select(ProductMappingExtensions.ToDtoProjection)
            .ToListAsync(cancellationToken);

        var ordered = summaryIds
            .Select(id => products.FirstOrDefault(p => p.Id == id))
            .Where(p => p != null)
            .Cast<ProductDto>()
            .ToList();

        return (ordered, null);
    }

    public static ProductFilterOptions? BuildRecommendationFilterOptions(
        GroqStructuredPlan plan,
        string question,
        out AiConsultantResponseDto? invalidFilterResponse)
    {
        invalidFilterResponse = null;

        ComponentType? componentType = null;
        if (!string.IsNullOrWhiteSpace(plan.ComponentType) &&
            Enum.TryParse<ComponentType>(plan.ComponentType.Trim(), ignoreCase: true, out var parsedComp))
        {
            componentType = parsedComp;
        }

        string? brandFilter = null;
        string? searchTerm = null;

        if (plan.TechnicalCriteria != null)
        {
            if (plan.TechnicalCriteria.TryGetValue("brand", out var rawBrand) && !string.IsNullOrWhiteSpace(rawBrand))
            {
                var trimmedBrand = rawBrand.Trim();
                // Do not treat GPU chipset vendor (AMD / NVIDIA) as board partner Brand filter
                if (componentType == ComponentType.Gpu && ChipsetVendors.Contains(trimmedBrand))
                {
                    searchTerm = trimmedBrand;
                }
                else
                {
                    brandFilter = trimmedBrand;
                }
            }

            if (plan.TechnicalCriteria.TryGetValue("chipset", out var rawChipset) && !string.IsNullOrWhiteSpace(rawChipset))
            {
                searchTerm = string.IsNullOrWhiteSpace(searchTerm)
                    ? rawChipset.Trim()
                    : $"{searchTerm} {rawChipset.Trim()}";
            }
        }

        CpuSocket? socket = null;
        RamType? memoryType = null;
        MotherboardFormFactor? mbFormFactor = null;
        StorageType? storageType = null;
        PsuEfficiency? psuEfficiency = null;
        CoolerType? coolerType = null;
        List<int>? vramGbs = null;
        List<int>? ramCapacities = null;
        List<int>? storageCapacities = null;
        List<int>? psuWattages = null;
        int? minPsuWattage = null;

        // Validate and map PSU wattage filters (minimum vs exact)
        var hasMinWattageCriteriaKey = plan.TechnicalCriteria != null &&
            (plan.TechnicalCriteria.ContainsKey("minWattage") || plan.TechnicalCriteria.ContainsKey("min_psu_wattage"));
        var hasExactWattageCriteriaKey = plan.TechnicalCriteria != null &&
            (plan.TechnicalCriteria.ContainsKey("wattage") || plan.TechnicalCriteria.ContainsKey("psuWattage"));

        var hasAnyWattageIntent =
            plan.MinPsuWattage.HasValue ||
            plan.ExactPsuWattage.HasValue ||
            hasMinWattageCriteriaKey ||
            hasExactWattageCriteriaKey;

        if (hasAnyWattageIntent)
        {
            if (componentType != ComponentType.Psu)
            {
                invalidFilterResponse = new AiConsultantResponseDto(
                    Status: "insufficient_data",
                    Answer: "Bộ lọc công suất nguồn (Wattage) chỉ áp dụng khi tìm kiếm Nguồn máy tính (PSU) với giá trị số nguyên dương hợp lệ.",
                    Products: [],
                    Sources: []);
                return null;
            }

            var isMinimumWattageIntent = IsMinimumWattageQuestion(question);

            if (plan.MinPsuWattage.HasValue)
            {
                if (plan.MinPsuWattage.Value <= 0)
                {
                    invalidFilterResponse = BuildInvalidWattageResponse();
                    return null;
                }
                minPsuWattage = plan.MinPsuWattage.Value;
            }
            else if (hasMinWattageCriteriaKey)
            {
                var rawMin = plan.TechnicalCriteria!.TryGetValue("minWattage", out var v1) ? v1 : plan.TechnicalCriteria["min_psu_wattage"];
                if (!TryParsePositiveWattage(rawMin, out var parsedMin))
                {
                    invalidFilterResponse = BuildInvalidWattageResponse();
                    return null;
                }
                minPsuWattage = parsedMin;
            }

            if (!minPsuWattage.HasValue)
            {
                if (plan.ExactPsuWattage.HasValue)
                {
                    if (plan.ExactPsuWattage.Value <= 0)
                    {
                        invalidFilterResponse = BuildInvalidWattageResponse();
                        return null;
                    }

                    if (isMinimumWattageIntent)
                    {
                        minPsuWattage = plan.ExactPsuWattage.Value;
                    }
                    else
                    {
                        psuWattages = [plan.ExactPsuWattage.Value];
                    }
                }
                else if (hasExactWattageCriteriaKey)
                {
                    var rawExact = plan.TechnicalCriteria!.TryGetValue("wattage", out var v1) ? v1 : plan.TechnicalCriteria["psuWattage"];
                    if (!TryParsePositiveWattage(rawExact, out var parsedWatt))
                    {
                        invalidFilterResponse = BuildInvalidWattageResponse();
                        return null;
                    }

                    if (isMinimumWattageIntent || rawExact.Contains(">=", StringComparison.Ordinal) || rawExact.Contains('+'))
                    {
                        minPsuWattage = parsedWatt;
                    }
                    else
                    {
                        psuWattages = [parsedWatt];
                    }
                }
            }
        }

        if (plan.TechnicalCriteria != null)
        {
            if (componentType is ComponentType.Cpu or ComponentType.Motherboard or ComponentType.Cooler &&
                plan.TechnicalCriteria.TryGetValue("socket", out var rawSocket) &&
                Enum.TryParse<CpuSocket>(rawSocket.Replace(" ", ""), ignoreCase: true, out var s))
            {
                socket = s;
            }

            if (componentType is ComponentType.Cpu or ComponentType.Motherboard or ComponentType.Ram &&
                plan.TechnicalCriteria.TryGetValue("memoryType", out var rawMem) &&
                Enum.TryParse<RamType>(rawMem.Trim(), ignoreCase: true, out var mt))
            {
                memoryType = mt;
            }

            if (componentType is ComponentType.Motherboard or ComponentType.Case &&
                plan.TechnicalCriteria.TryGetValue("formFactor", out var rawFf) &&
                Enum.TryParse<MotherboardFormFactor>(rawFf.Trim(), ignoreCase: true, out var ff))
            {
                mbFormFactor = ff;
            }

            if (componentType == ComponentType.Storage &&
                plan.TechnicalCriteria.TryGetValue("storageType", out var rawSt) &&
                Enum.TryParse<StorageType>(rawSt.Trim(), ignoreCase: true, out var st))
            {
                storageType = st;
            }

            if (componentType == ComponentType.Psu &&
                plan.TechnicalCriteria.TryGetValue("efficiency", out var rawEff) &&
                Enum.TryParse<PsuEfficiency>(rawEff.Trim(), ignoreCase: true, out var eff))
            {
                psuEfficiency = eff;
            }

            if (componentType == ComponentType.Cooler &&
                plan.TechnicalCriteria.TryGetValue("coolerType", out var rawCt) &&
                Enum.TryParse<CoolerType>(rawCt.Trim(), ignoreCase: true, out var ct))
            {
                coolerType = ct;
            }

            if (componentType == ComponentType.Gpu &&
                plan.TechnicalCriteria.TryGetValue("vramGb", out var rawVram) &&
                int.TryParse(ExtractDigits(rawVram), out var vram) && vram > 0)
            {
                vramGbs = [vram];
            }

            if (componentType == ComponentType.Ram &&
                plan.TechnicalCriteria.TryGetValue("capacityGb", out var rawRamCap) &&
                int.TryParse(ExtractDigits(rawRamCap), out var ramCap) && ramCap > 0)
            {
                ramCapacities = [ramCap];
            }

            if (componentType == ComponentType.Storage &&
                plan.TechnicalCriteria.TryGetValue("capacityGb", out var rawStoCap) &&
                int.TryParse(ExtractDigits(rawStoCap), out var stoCap) && stoCap > 0)
            {
                storageCapacities = [stoCap];
            }
        }

        decimal? minPrice = plan.MinPrice is >= 0 ? plan.MinPrice : null;
        decimal? maxPrice = plan.MaxPrice is > 0 ? plan.MaxPrice : null;
        if (minPrice.HasValue && maxPrice.HasValue && minPrice.Value > maxPrice.Value)
        {
            (minPrice, maxPrice) = (maxPrice, minPrice);
        }

        return new ProductFilterOptions(
            PageIndex: 1,
            PageSize: 5,
            SearchTerm: searchTerm,
            ComponentType: componentType,
            Brand: brandFilter,
            MinPrice: minPrice,
            MaxPrice: maxPrice,
            InStock: plan.InStockOnly ?? true,
            IsActive: true,
            SortBy: ProductSortOption.PriceLowToHigh,
            Socket: socket,
            MemoryType: memoryType,
            MotherboardFormFactor: mbFormFactor,
            StorageType: storageType,
            PsuEfficiency: psuEfficiency,
            CoolerType: coolerType,
            VramGbs: vramGbs,
            RamCapacitiesGb: ramCapacities,
            StorageCapacitiesGb: storageCapacities,
            PsuWattages: psuWattages,
            MinPsuWattage: minPsuWattage);
    }

    private static bool IsMinimumWattageQuestion(string? question)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return false;
        }

        return Regex.IsMatch(
            question,
            @"(trở\s+lên|tối\s+thiểu|ít\s+nhất|>=\s*\d+|\btrên\s+\d+\s*w|\bfrom\s+\d+\s*w|\bat\s+least\s+\d+\s*w)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool TryParsePositiveWattage(string? rawValue, out int wattage)
    {
        wattage = 0;
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        var trimmed = rawValue.Trim();
        if (trimmed.StartsWith('-'))
        {
            return false;
        }

        var digits = ExtractDigits(trimmed);
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out wattage) && wattage > 0;
    }

    private static AiConsultantResponseDto BuildInvalidWattageResponse() =>
        new(
            Status: "insufficient_data",
            Answer: "Mức công suất nguồn (Wattage) yêu cầu không hợp lệ. Bạn vui lòng nhập mức công suất là số nguyên dương (ví dụ: 650W hoặc từ 650W trở lên).",
            Products: [],
            Sources: []);

    private static string NormalizeTaskType(string? rawTaskType)
    {
        if (string.IsNullOrWhiteSpace(rawTaskType))
            return "product_info";

        var normalized = rawTaskType.Trim().ToLowerInvariant();
        return normalized switch
        {
            "product_info" or "compare" or "recommend" or "faq" or "clarify" or "out_of_scope" => normalized,
            _ => "product_info"
        };
    }

    private static string ExtractDigits(string input)
    {
        return new string(input.Where(char.IsDigit).ToArray());
    }

    private static AiProductDto MapToAiProductDto(ProductDto p) =>
        new(
            Sku: p.Sku,
            Name: p.Name,
            Slug: p.Slug,
            Url: $"/products/{p.Slug}",
            PriceVnd: p.Price,
            InStock: p.StockQuantity > 0,
            IsActive: p.IsActive);

    private static List<AiSourceDto> BuildSources(
        IReadOnlyList<ProductDto> catalogProducts,
        HashSet<string> catalogFactKeys,
        IReadOnlyList<string> supportingKeys,
        IReadOnlyList<FaqEntry> matchedFaqs,
        IReadOnlyList<GroqWebSearchResult> webFacts)
    {
        var sources = new List<AiSourceDto>();

        if (catalogProducts.Count > 0)
        {
            var usedCatalogKeys = supportingKeys
                .Where(k => !CuratedFaqRepository.IsPolicyFactKey(k) && catalogFactKeys.Contains(k))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (usedCatalogKeys.Count == 0)
            {
                usedCatalogKeys = catalogFactKeys
                    .Where(k => !CuratedFaqRepository.IsPolicyFactKey(k))
                    .Take(6)
                    .ToList();
            }

            if (usedCatalogKeys.Count > 0)
            {
                sources.Add(new AiSourceDto(
                    Kind: "catalog",
                    Title: "Danh mục sản phẩm RiuTek",
                    Url: null,
                    FactKeys: usedCatalogKeys));
            }
        }

        foreach (var faq in matchedFaqs)
        {
            sources.Add(new AiSourceDto(
                Kind: "faq",
                Title: faq.Title,
                Url: null,
                FactKeys: [faq.Key]));
        }

        var groupedWebFacts = webFacts
            .Where(wf => !string.IsNullOrWhiteSpace(wf.SourceUrl) &&
                         !string.IsNullOrWhiteSpace(wf.FactKey) &&
                         !CuratedFaqRepository.IsPolicyFactKey(wf.FactKey))
            .GroupBy(
                wf => $"{wf.ProductKey ?? string.Empty}|{wf.SourceUrl.Trim()}",
                StringComparer.OrdinalIgnoreCase);

        foreach (var grp in groupedWebFacts)
        {
            var verifiedFactKeys = grp
                .Select(wf => wf.FactKey)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (verifiedFactKeys.Count == 0)
            {
                continue;
            }

            var first = grp.First();
            var baseTitle = string.IsNullOrWhiteSpace(first.SourceTitle)
                ? "Thông số kỹ thuật từ hãng"
                : first.SourceTitle.Trim();

            var titleWithModel = !string.IsNullOrWhiteSpace(first.ModelName) &&
                                 !baseTitle.Contains(first.ModelName, StringComparison.OrdinalIgnoreCase)
                ? $"{baseTitle} ({first.ModelName})"
                : baseTitle;

            sources.Add(new AiSourceDto(
                Kind: "manufacturer",
                Title: titleWithModel,
                Url: first.SourceUrl,
                FactKeys: verifiedFactKeys));
        }

        return sources;
    }

    private static string BuildDeterministicFallbackAnswer(
        IReadOnlyList<ProductDto> catalogProducts,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> productFactsBySku,
        IReadOnlyList<GroqWebSearchResult> webFacts,
        IReadOnlyList<FaqEntry> matchedFaqs,
        IReadOnlyList<string> unresolvedMissingFacts)
    {
        var parts = new List<string>();

        foreach (var p in catalogProducts)
        {
            var stockText = p.StockQuantity > 0 ? "còn hàng" : "tạm hết hàng";
            var priceText = string.Format(CultureInfo.InvariantCulture, "{0:N0} VNĐ", p.Price);
            parts.Add($"{p.Name} (SKU: {p.Sku}) hiện có giá {priceText} ({stockText}).");
        }

        foreach (var wf in webFacts)
        {
            parts.Add($"Thông tin kỹ thuật ({wf.FactKey}): {wf.Summary}.");
        }

        foreach (var faq in matchedFaqs)
        {
            parts.Add(faq.Content);
        }

        if (unresolvedMissingFacts.Count > 0)
        {
            parts.Add($"Một số thông số kỹ thuật chi tiết ({string.Join(", ", unresolvedMissingFacts)}) hiện chưa có đủ dữ liệu xác minh trong hệ thống.");
        }

        return string.Join(" ", parts);
    }

    private void LogMetadata(
        string requestId,
        int analysisCalls,
        int webCalls,
        int synthesisCalls,
        long elapsedMs,
        string status)
    {
        _logger.LogInformation(
            "AI Consultant turn completed. RequestId={RequestId} AnalysisCalls={AnalysisCalls} WebCalls={WebCalls} SynthesisCalls={SynthesisCalls} ElapsedMs={ElapsedMs} Status={Status}",
            requestId,
            analysisCalls,
            webCalls,
            synthesisCalls,
            elapsedMs,
            status);
    }
}

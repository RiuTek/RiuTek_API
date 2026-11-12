using System.Diagnostics;
using System.Globalization;
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

            // 3. Resolve catalog products & FAQ
            var matchedFaqs = CuratedFaqRepository.FindMatching(question, plan.RequiredFacts);

            if (taskType == "faq" && (plan.TargetProducts == null || plan.TargetProducts.Count == 0) && string.IsNullOrWhiteSpace(productSlug))
            {
                if (matchedFaqs.Count == 0)
                {
                    var noFaqResponse = new AiConsultantResponseDto(
                        Status: "insufficient_data",
                        Answer: "Hiện tại RiuTek chưa có thông tin chính sách chi tiết cho câu hỏi này. Bạn vui lòng liên hệ bộ phận chăm sóc khách hàng để được hỗ trợ trực tiếp.",
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

            // 5. Determine missing technical facts and web search eligibility
            var missingTechnicalFacts = new List<string>();
            if (catalogProducts.Count > 0)
            {
                foreach (var prod in catalogProducts)
                {
                    var missingForProd = ProductSpecFactExtractor.FindMissingTechnicalFacts(
                        plan.RequiredFacts,
                        productFactsBySku[prod.Sku]);
                    foreach (var mf in missingForProd)
                    {
                        if (!missingTechnicalFacts.Contains(mf, StringComparer.OrdinalIgnoreCase))
                        {
                            missingTechnicalFacts.Add(mf);
                        }
                    }
                }
            }
            else if (plan.RequiredFacts is { Count: > 0 })
            {
                foreach (var rf in plan.RequiredFacts)
                {
                    if (ProductSpecFactExtractor.IsWebSearchableTechnicalFact(rf) &&
                        !missingTechnicalFacts.Contains(rf, StringComparer.OrdinalIgnoreCase))
                    {
                        missingTechnicalFacts.Add(rf);
                    }
                }
            }

            var webFacts = new List<GroqWebSearchResult>();
            var webBranchDegraded = false;

            var canIdentifyModelForWeb = DetermineModelForWebSearch(catalogProducts, plan.TargetProducts);
            var isEligibleForWebSearch =
                (taskType is "product_info" or "compare") &&
                missingTechnicalFacts.Count > 0 &&
                !string.IsNullOrWhiteSpace(canIdentifyModelForWeb);

            if (isEligibleForWebSearch)
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
                                canIdentifyModelForWeb!,
                                missingTechnicalFacts,
                                cancellationToken);

                            if (webResult.IsSuccess && webResult.Value.Count > 0)
                            {
                                webFacts.AddRange(webResult.Value);
                            }
                            else
                            {
                                webBranchDegraded = true;
                            }
                        }
                    }
                }
            }

            var unresolvedMissingFacts = missingTechnicalFacts
                .Where(mf => !webFacts.Any(wf => string.Equals(wf.FactKey, mf, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            var hasUnresolvedFacts = unresolvedMissingFacts.Count > 0 || webBranchDegraded;

            // If no catalog products, no FAQ, and no verified web facts
            if (catalogProducts.Count == 0 && matchedFaqs.Count == 0 && webFacts.Count == 0)
            {
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
                int.TryParse( ExtractDigits(rawVram), out var vram) && vram > 0)
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

            if (componentType == ComponentType.Psu &&
                plan.TechnicalCriteria.TryGetValue("wattage", out var rawWatt) &&
                int.TryParse(ExtractDigits(rawWatt), out var watt) && watt > 0)
            {
                psuWattages = [watt];
            }
        }

        decimal? minPrice = plan.MinPrice is >= 0 ? plan.MinPrice : null;
        decimal? maxPrice = plan.MaxPrice is > 0 ? plan.MaxPrice : null;
        if (minPrice.HasValue && maxPrice.HasValue && minPrice.Value > maxPrice.Value)
        {
            (minPrice, maxPrice) = (maxPrice, minPrice);
        }

        var filterOptions = new ProductFilterOptions(
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
            PsuWattages: psuWattages);

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

    private static string? DetermineModelForWebSearch(
        IReadOnlyList<ProductDto> catalogProducts,
        IReadOnlyList<string>? targetProducts)
    {
        if (catalogProducts.Count > 0)
        {
            var p = catalogProducts[0];
            return $"{p.Brand} {p.Name}".Trim();
        }

        if (targetProducts is { Count: 1 })
        {
            var candidate = targetProducts[0].Trim();
            // Require at least 2 tokens (e.g. brand/series + model number) so vague queries don't trigger web search
            var tokens = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length >= 2 && candidate.Any(char.IsDigit))
            {
                return candidate;
            }
        }

        return null;
    }

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
                .Where(k => catalogFactKeys.Contains(k))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (usedCatalogKeys.Count == 0)
            {
                usedCatalogKeys = catalogFactKeys.Take(6).ToList();
            }

            sources.Add(new AiSourceDto(
                Kind: "catalog",
                Title: "Danh mục sản phẩm RiuTek",
                Url: null,
                FactKeys: usedCatalogKeys));
        }

        foreach (var faq in matchedFaqs)
        {
            sources.Add(new AiSourceDto(
                Kind: "faq",
                Title: faq.Title,
                Url: null,
                FactKeys: [faq.Key]));
        }

        foreach (var wf in webFacts)
        {
            if (!string.IsNullOrWhiteSpace(wf.SourceUrl))
            {
                sources.Add(new AiSourceDto(
                    Kind: "manufacturer",
                    Title: string.IsNullOrWhiteSpace(wf.SourceTitle) ? "Thông số kỹ thuật từ hãng" : wf.SourceTitle,
                    Url: wf.SourceUrl,
                    FactKeys: [wf.FactKey]));
            }
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

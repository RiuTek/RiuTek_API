using RiuTek.Application.Common.Interfaces;

namespace RiuTek.Application.Features.Ai.Common;

public static class CuratedFaqRepository
{
    // Approved FAQ entries are intentionally empty until store owner publishes verified policies.
    private static readonly IReadOnlyList<FaqEntry> Entries = [];

    private static readonly IReadOnlyDictionary<string, string[]> PolicyIntentKeywords =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["shipping"] = ["shipping", "giao hàng", "vận chuyển", "ship", "thời gian giao", "phí ship", "nhận hàng", "delivery"],
            ["warranty"] = ["warranty", "bảo hành", "lỗi kỹ thuật", "sửa chữa", "thời hạn bảo hành"],
            ["returns"] = ["returns", "return", "refund", "đổi trả", "hoàn tiền", "đổi mới", "trả hàng", "lỗi đổi"],
            ["payment"] = ["payment", "thanh toán", "trả góp", "cod", "stripe", "chuyển khoản"],
            ["policy"] = ["policy", "chính sách"]
        };

    public static IReadOnlyList<FaqEntry> GetAll() => Entries;

    public static IReadOnlyList<FaqEntry> FindMatching(string question, IEnumerable<string>? requestedFactKeys = null)
    {
        if (Entries.Count == 0)
        {
            return [];
        }

        var normalizedQuestion = (question ?? string.Empty).Trim().ToLowerInvariant();
        var keysSet = requestedFactKeys?
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim().ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];

        var matched = new List<FaqEntry>();
        foreach (var entry in Entries)
        {
            if (keysSet.Contains(entry.Key) ||
                entry.Keywords.Any(kw => normalizedQuestion.Contains(kw, StringComparison.OrdinalIgnoreCase)))
            {
                matched.Add(entry);
            }
        }

        return matched;
    }

    public static IReadOnlyList<string> DetectUnpublishedPolicyKeys(
        string question,
        IEnumerable<string>? requestedFactKeys = null)
    {
        var normalizedQuestion = (question ?? string.Empty).Trim().ToLowerInvariant();
        var keysSet = requestedFactKeys?
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim().ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];

        var detected = new List<string>();
        foreach (var kvp in PolicyIntentKeywords)
        {
            var policyKey = kvp.Key;
            var hasApprovedEntry = Entries.Any(e => string.Equals(e.Key, policyKey, StringComparison.OrdinalIgnoreCase));
            if (hasApprovedEntry)
            {
                continue;
            }

            if (keysSet.Contains(policyKey) ||
                kvp.Value.Any(kw =>
                    keysSet.Contains(kw) ||
                    normalizedQuestion.Contains(kw, StringComparison.OrdinalIgnoreCase)))
            {
                detected.Add(policyKey);
            }
        }

        return detected;
    }

    public static bool IsPolicyFactKey(string? factKey)
    {
        if (string.IsNullOrWhiteSpace(factKey))
        {
            return false;
        }

        var trimmed = factKey.Trim();
        return PolicyIntentKeywords.ContainsKey(trimmed) ||
               PolicyIntentKeywords.Values.Any(kws => kws.Contains(trimmed, StringComparer.OrdinalIgnoreCase));
    }

    public static string BuildStandaloneUnpublishedPolicyAnswer() =>
        "Dạ, mình chưa có thông tin chính thức từ RiuTek về chính sách bạn hỏi. Bạn muốn mình kiểm tra thêm giá, tình trạng hàng hoặc thông số của sản phẩm nào không?";

    public static string BuildMixedUnpublishedPolicyNotice() =>
        "Về chính sách bạn hỏi, thông tin này hiện chưa được công bố trong phần tư vấn của RiuTek.";
}

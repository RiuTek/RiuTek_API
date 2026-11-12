using RiuTek.Application.Common.Interfaces;

namespace RiuTek.Application.Features.Ai.Common;

public static class CuratedFaqRepository
{
    private static readonly IReadOnlyList<FaqEntry> Entries =
    [
        new(
            Key: "shipping",
            Title: "Chính sách giao hàng RiuTek",
            Content: "RiuTek hỗ trợ giao hàng toàn quốc. Đơn hàng nội thành nhận trong 1–2 ngày làm việc, ngoại tỉnh 2–4 ngày làm việc. Khách hàng được kiểm tra ngoại quan linh kiện trước khi nhận hàng.",
            Keywords: ["giao hàng", "vận chuyển", "ship", "thời gian giao", "phí ship", "nhận hàng", "delivery", "shipping"]),
        new(
            Key: "warranty",
            Title: "Chính sách bảo hành RiuTek",
            Content: "Linh kiện chính hãng tại RiuTek được bảo hành theo tiêu chuẩn nhà sản xuất (thông thường 24–36 tháng tùy loại linh kiện như CPU, Mainboard, VGA, RAM, PSU, SSD). Bảo hành áp dụng cho lỗi kỹ thuật từ nhà sản xuất với tem/serial nguyên vẹn.",
            Keywords: ["bảo hành", "warranty", "lỗi kỹ thuật", "sửa chữa", "thời hạn bảo hành"]),
        new(
            Key: "returns",
            Title: "Chính sách đổi trả RiuTek",
            Content: "RiuTek hỗ trợ đổi mới trong 15 ngày đầu nếu sản phẩm phát sinh lỗi kỹ thuật do nhà sản xuất, với điều kiện sản phẩm còn đầy đủ hộp, phụ kiện và không bị hư hại vật lý do người dùng.",
            Keywords: ["đổi trả", "hoàn tiền", "đổi mới", "trả hàng", "return", "refund", "lỗi đổi"]),
        new(
            Key: "payment",
            Title: "Phương thức thanh toán RiuTek",
            Content: "RiuTek hỗ trợ thanh toán khi nhận hàng (COD) và thanh toán trực tuyến qua cổng Stripe Checkout.",
            Keywords: ["thanh toán", "trả góp", "cod", "stripe", "chuyển khoản", "payment"])
    ];

    public static IReadOnlyList<FaqEntry> GetAll() => Entries;

    public static IReadOnlyList<FaqEntry> FindMatching(string question, IEnumerable<string>? requestedFactKeys = null)
    {
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
}

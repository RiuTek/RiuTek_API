using System.Globalization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Core.Entities;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Features.Ai.Queries;

public record RecommendAiBuildQuery(GroqStructuredPlan Plan) : IRequest<AiConsultantResponseDto>;

public sealed class RecommendAiBuildQueryHandler(
    IApplicationDbContext context,
    IHardwareCompatibilityService compatibilityService) : IRequestHandler<RecommendAiBuildQuery, AiConsultantResponseDto>
{
    private const int CatalogLimitPerSlot = 24;
    private const int SearchLimit = 12_000;

    private static readonly ComponentType[] BuildSlots =
    [
        ComponentType.Cpu, ComponentType.Motherboard, ComponentType.Ram, ComponentType.Storage,
        ComponentType.Gpu, ComponentType.Psu, ComponentType.Case, ComponentType.Cooler
    ];

    private static readonly AiSourceDto CatalogSource = new(
        "catalog", "Danh mục sản phẩm RiuTek", null, ["priceVnd", "inStock", "specifications"]);

    public async Task<AiConsultantResponseDto> Handle(RecommendAiBuildQuery request, CancellationToken cancellationToken)
    {
        var plan = request.Plan;
        var useCase = plan.BuildUseCase?.Trim().ToLowerInvariant();
        if (useCase is not ("office" or "gaming" or "creation"))
        {
            return Clarify(plan.MaxPrice is > 0
                ? "Dạ, bạn dùng máy chủ yếu để làm văn phòng, chơi game hay làm đồ họa ạ?"
                : "Dạ, bạn cho mình biết nhu cầu chính (văn phòng, chơi game hay làm đồ họa) và ngân sách tối đa cho phần linh kiện cần mua nhé.");
        }
        if (plan.MaxPrice is not > 0) return Clarify("Dạ, ngân sách tối đa cho các linh kiện cần mua là bao nhiêu ạ?");
        if (!TryReadMinimum(plan.TechnicalCriteria, "minRamGb", out var minRam) ||
            !TryReadMinimum(plan.TechnicalCriteria, "minStorageGb", out var minStorage))
        {
            return Clarify("Dạ, bạn xác nhận giúp mình dung lượng RAM hoặc ổ lưu trữ mong muốn theo GB nhé.");
        }

        bool MeetsCapacity(Product part) => part.Specifications switch
        {
            RamSpecification ram => ram.CapacityGb >= minRam,
            StorageSpecification storage => storage.CapacityGb >= minStorage,
            _ => true
        };

        var budget = plan.MaxPrice.Value;
        var ownedKeys = NormalizeKeys(plan.OwnedProducts);
        var requiredKeys = NormalizeKeys(plan.TargetProducts);
        if (ownedKeys.Length + requiredKeys.Length > BuildSlots.Length || ownedKeys.Intersect(requiredKeys).Any())
        {
            return Clarify("Dạ, bạn xác nhận giúp mình mỗi loại linh kiện một mẫu và cho biết mẫu nào đã có, mẫu nào cần mua nhé.");
        }

        var keys = ownedKeys.Concat(requiredKeys).ToArray();
        var catalog = context.Products.AsNoTracking();
        var namedParts = keys.Length == 0 ? new List<Product>() : await catalog
            .Where(p => keys.Contains(p.Sku.ToLower()) || keys.Contains(p.Slug.ToLower()) || keys.Contains(p.Name.ToLower()))
            .ToListAsync(cancellationToken);
        var fixedParts = new Dictionary<ComponentType, Product>();
        var ownedIds = new HashSet<Guid>();

        foreach (var key in keys)
        {
            var matching = namedParts.Where(p => string.Equals(p.Sku, key, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.Slug, key, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase)).ToList();

            if (matching.Count != 1 || !BuildSlots.Contains(matching[0].ComponentType) || !HasMatchingSpecs(matching[0]))
            {
                return Clarify("Dạ, mình chưa xác định được chính xác một linh kiện bạn muốn dùng từ catalog RiuTek. Bạn gửi giúp mình tên đầy đủ hoặc mã SKU để mình kiểm tra nhé.");
            }

            var part = matching[0];
            if (!MeetsCapacity(part)) return Clarify("Dạ, mẫu bạn muốn giữ có dung lượng thấp hơn yêu cầu. Bạn muốn đổi mẫu hay giữ dung lượng hiện tại ạ?");
            if (!fixedParts.TryAdd(part.ComponentType, part))
            {
                return Clarify("Dạ, hiện mình chỉ đề xuất một mẫu cho mỗi loại linh kiện. Bạn muốn giữ mẫu nào cho loại đang có nhiều lựa chọn ạ?");
            }

            if (ownedKeys.Contains(key)) ownedIds.Add(part.Id);
            else if (!part.IsActive || part.StockQuantity < 1 || part.Price < 0 || part.Price > budget)
            {
                return Unavailable("Dạ, có linh kiện bạn yêu cầu mua đang không khả dụng hoặc vượt ngân sách. Bạn muốn đổi sang mẫu khác hay điều chỉnh ngân sách ạ?");
            }
        }

        var slots = useCase switch
        {
            "gaming" => BuildSlots.OrderBy(type => type == ComponentType.Gpu ? 0 : 1).ToArray(),
            "creation" => BuildSlots.OrderBy(type => type == ComponentType.Ram ? 0 : 1).ToArray(),
            _ => BuildSlots.OrderBy(type => type == ComponentType.Gpu ? 1 : 0).ToArray()
        };
        var purchasable = catalog.Where(p => p.IsActive && p.StockQuantity > 0 && p.Price >= 0 && p.Price <= budget);
        var candidates = new List<Product?>[slots.Length];
        for (var index = 0; index < slots.Length; index++)
        {
            var slot = slots[index];
            if (fixedParts.TryGetValue(slot, out var fixedPart))
            {
                candidates[index] = [fixedPart];
                continue;
            }

            var available = await purchasable.Where(p => p.ComponentType == slot)
                .OrderBy(p => p.Price).ThenBy(p => p.Id).Take(CatalogLimitPerSlot)
                .ToListAsync(cancellationToken);
            var ordered = available.Where(p => HasMatchingSpecs(p) && MeetsCapacity(p))
                .OrderByDescending(p => Preference(p.Specifications, useCase))
                .ThenBy(p => p.Price)
                .Cast<Product?>().ToList();
            if (slot == ComponentType.Gpu && useCase == "office") ordered.Insert(0, null);
            if (ordered.Count == 0)
            {
                return Unavailable($"Dạ, hiện mình chưa có {ComponentLabel(slot)} phù hợp trong khoảng giá và còn hàng để ghép đủ bộ. Bạn muốn mình tư vấn điều chỉnh yêu cầu nào trước ạ?");
            }
            candidates[index] = ordered;
        }

        decimal Cost(Product? part) => part is null || ownedIds.Contains(part.Id) ? 0 : part.Price;

        var minimumRemainingCost = new decimal[candidates.Length + 1];
        for (var i = candidates.Length - 1; i >= 0; i--)
            minimumRemainingCost[i] = minimumRemainingCost[i + 1] + candidates[i].Min(Cost);

        var selected = new List<Product>(slots.Length);
        var visited = 0;
        CompatibilityCheckResultDto? verified = null;

        bool Search(int index, decimal spend)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (visited >= SearchLimit || spend + minimumRemainingCost[index] > budget) return false;
            if (index == slots.Length)
            {
                var result = compatibilityService.ValidateComponents(selected);
                if (!result.IsCompleteSystem || !result.IsCompatible || result.Issues.Count != 0) return false;
                verified = result;
                return true;
            }

            foreach (var part in candidates[index])
            {
                if (visited++ >= SearchLimit) return false;
                var nextSpend = spend + Cost(part);
                if (nextSpend + minimumRemainingCost[index + 1] > budget) continue;

                if (part != null) selected.Add(part);
                if (StorageFits(selected))
                {
                    var partial = compatibilityService.ValidateComponents(selected);
                    // The display warning can be resolved by a GPU in a later slot.
                    if (partial.IsCompatible && partial.Issues.All(issue => issue.RuleId == "COMP-12") &&
                        Search(index + 1, nextSpend)) return true;
                }
                if (part != null) selected.RemoveAt(selected.Count - 1);
            }
            return false;
        }

        if (!Search(0, 0) || verified == null)
        {
            return Unavailable("Dạ, mình chưa tìm được một bộ linh kiện đầy đủ vượt qua kiểm tra tương thích trong ngân sách và yêu cầu này. Bạn muốn điều chỉnh ngân sách hay thay một linh kiện đã chọn ạ?");
        }

        var items = selected.OrderBy(p => Array.IndexOf(BuildSlots, p.ComponentType)).Select(p => new AiBuildItemDto(
            new AiProductDto(p.Sku, p.Name, p.Slug, $"/products/{p.Slug}", p.Price, p.StockQuantity > 0, p.IsActive, p.ImageUrl),
            ComponentLabel(p.ComponentType),
            DescribePart(p.Specifications),
            ownedIds.Contains(p.Id))).ToList();
        var purpose = useCase switch { "gaming" => "chơi game", "creation" => "đồ họa và sáng tạo nội dung", _ => "văn phòng" };

        return new AiConsultantResponseDto(
            "answered",
            $"Dạ, mình đề xuất một bộ máy cho nhu cầu {purpose}. Các linh kiện bên dưới đã qua kiểm tra tương thích theo thông số RiuTek đang có.",
            [], [CatalogSource],
            new AiBuildDto(budget, selected.Sum(Cost), verified.RecommendedPsuWattage, items));
    }

    private static string[] NormalizeKeys(IReadOnlyList<string>? values) => (values ?? [])
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value.Trim().ToLowerInvariant()).Distinct().ToArray();

    private static bool HasMatchingSpecs(Product part) => part.Specifications?.ComponentType == part.ComponentType;

    private static bool TryReadMinimum(IReadOnlyDictionary<string, string>? criteria, string key, out int minimum)
    {
        minimum = 0;
        return criteria == null || !criteria.TryGetValue(key, out var raw) ||
               (int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out minimum) && minimum > 0);
    }

    private static bool StorageFits(List<Product> parts)
    {
        var motherboard = parts.Select(p => p.Specifications).OfType<MotherboardSpecification>().FirstOrDefault();
        var storage = parts.Select(p => p.Specifications).OfType<StorageSpecification>().FirstOrDefault();
        return motherboard is null || storage is null ||
               (storage.StorageType == StorageType.NVMe_M2 ? motherboard.M2Slots > 0 : motherboard.SataPorts > 0);
    }

    private static int Preference(ComponentSpecification specs, string useCase) => specs switch
    {
        CpuSpecification { HasIntegratedGpu: true } when useCase == "office" => 1,
        CpuSpecification cpu when useCase == "creation" => cpu.CoreCount,
        GpuSpecification gpu when useCase is "gaming" or "creation" => gpu.VramGb,
        RamSpecification ram when useCase is "gaming" or "creation" => Math.Min(ram.CapacityGb, useCase == "creation" ? 32 : 16),
        StorageSpecification { StorageType: StorageType.NVMe_M2 or StorageType.SATA_SSD } => 1,
        _ => 0
    };

    private static string DescribePart(ComponentSpecification specs) => specs switch
    {
        CpuSpecification { CoreCount: > 0 } cpu => $"{cpu.CoreCount} nhân, socket {cpu.Socket}{(cpu.HasIntegratedGpu ? ", có đồ họa tích hợp" : "")}.",
        MotherboardSpecification board => $"Socket {board.Socket}, hỗ trợ RAM {board.MemoryType}.",
        RamSpecification { CapacityGb: > 0 } ram => $"Bộ RAM {ram.CapacityGb} GB chuẩn {ram.MemoryType}.",
        StorageSpecification { CapacityGb: > 0 } storage => $"Dung lượng {storage.CapacityGb} GB.",
        GpuSpecification { VramGb: > 0 } gpu => $"Card đồ họa rời với {gpu.VramGb} GB VRAM.",
        PsuSpecification { Wattage: > 0 } psu => $"Nguồn {psu.Wattage} W cho bộ máy này.",
        CaseSpecification => "Vỏ máy được đối chiếu với kích thước linh kiện trong bộ.",
        CoolerSpecification => "Tản nhiệt được đối chiếu với socket, CPU và vỏ máy.",
        _ => "Linh kiện thuộc bộ cấu hình đã được kiểm tra."
    };

    private static string ComponentLabel(ComponentType type) => type switch
    {
        ComponentType.Cpu => "CPU",
        ComponentType.Motherboard => "Bo mạch chủ",
        ComponentType.Ram => "RAM",
        ComponentType.Storage => "Ổ lưu trữ",
        ComponentType.Gpu => "Card đồ họa",
        ComponentType.Psu => "Nguồn",
        ComponentType.Case => "Vỏ máy",
        ComponentType.Cooler => "Tản nhiệt CPU",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    private static AiConsultantResponseDto Clarify(string answer) => new("needs_clarification", answer, [], []);

    private static AiConsultantResponseDto Unavailable(string answer) => new("insufficient_data", answer, [], [CatalogSource]);
}

using System.Globalization;
using RiuTek.Application.DTOs;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Features.Ai.Common;

public static class ProductSpecFactExtractor
{
    private static readonly HashSet<string> NonWebSearchableFacts = new(StringComparer.OrdinalIgnoreCase)
    {
        "price", "priceVnd", "gia", "giá",
        "stock", "inStock", "stockQuantity", "tonkho", "tồn kho", "conhang", "còn hàng",
        "isActive", "status", "trangthai", "trạng thái",
        "sku", "slug", "url", "id",
        "shipping", "warranty", "returns", "payment", "policy",
        "giao hàng", "bảo hành", "đổi trả", "thanh toán", "chính sách"
    };

    private static readonly Dictionary<string, string[]> FactAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["socket"] = ["socket", "cpu_socket", "supportedSockets"],
        ["cores"] = ["cores", "coreCount", "so_nhan"],
        ["threads"] = ["threads", "threadCount", "so_luong"],
        ["baseClock"] = ["baseClock", "baseClockGhz", "xung_co_ban"],
        ["boostClock"] = ["boostClock", "boostClockGhz", "xung_boost"],
        ["tdp"] = ["tdp", "tdpWattage", "maxTdpRating", "cong_suat_toa_nhiet"],
        ["igpu"] = ["igpu", "hasIntegratedGpu", "gpu_tich_hop"],
        ["memoryType"] = ["memoryType", "supportedMemoryType", "loai_ram"],
        ["maxMemorySpeed"] = ["maxMemorySpeed", "maxMemorySpeedMhz"],
        ["chipset"] = ["chipset"],
        ["formFactor"] = ["formFactor", "supportedFormFactors", "kich_thuoc"],
        ["memorySlots"] = ["memorySlots", "khe_ram"],
        ["maxMemoryCapacity"] = ["maxMemoryCapacity", "maxMemoryCapacityGb"],
        ["m2Slots"] = ["m2Slots", "khe_m2"],
        ["sataPorts"] = ["sataPorts", "cong_sata"],
        ["pcieSlots"] = ["pcieSlots", "khe_pcie"],
        ["wifi"] = ["wifi", "hasWifi"],
        ["vram"] = ["vram", "vramGb", "dung_luong_vram"],
        ["vramType"] = ["vramType", "loai_vram"],
        ["lengthMm"] = ["lengthMm", "maxGpuLengthMm", "maxPsuLengthMm", "chieu_dai"],
        ["slotWidth"] = ["slotWidth", "do_day_slot"],
        ["recommendedPsu"] = ["recommendedPsu", "recommendedPsuWattage", "nguon_de_nghi"],
        ["12vhpwr"] = ["12vhpwr", "requires12VHPWR", "has12VHPWR"],
        ["powerConnectors"] = ["powerConnectors", "dau_nguon", "cong_nguon_phu"],
        ["capacityGb"] = ["capacityGb", "dung_luong"],
        ["moduleCount"] = ["moduleCount", "so_thanh"],
        ["speedMhz"] = ["speedMhz", "bus_ram", "toc_do_ram"],
        ["casLatency"] = ["casLatency", "cl", "do_tre"],
        ["rgb"] = ["rgb", "hasRgb"],
        ["storageType"] = ["storageType", "loai_o_cung"],
        ["interface"] = ["interface", "storageInterface", "chuan_giao_tiep"],
        ["readSpeed"] = ["readSpeed", "readSpeedMBs", "toc_do_doc"],
        ["writeSpeed"] = ["writeSpeed", "writeSpeedMBs", "toc_do_ghi"],
        ["wattage"] = ["wattage", "cong_suat"],
        ["efficiency"] = ["efficiency", "psuEfficiency", "chuan_80_plus"],
        ["modularity"] = ["modularity", "psuModularity", "full_modular"],
        ["maxCoolerHeight"] = ["maxCoolerHeight", "maxCpuCoolerHeightMm", "heightMm", "chieu_cao_tan"],
        ["radiatorSize"] = ["radiatorSize", "radiatorSizeMm", "supportedRadiatorSizesMm", "kich_thuoc_rad"],
        ["includedFans"] = ["includedFans", "quat_kem_theo"],
        ["coolerType"] = ["coolerType", "loai_tan"]
    };

    public static IReadOnlyDictionary<string, string> ExtractCatalogFacts(ProductDto product)
    {
        var facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sku"] = product.Sku,
            ["name"] = product.Name,
            ["brand"] = product.Brand,
            ["componentType"] = product.ComponentType.ToString(),
            ["priceVnd"] = product.Price.ToString("0", CultureInfo.InvariantCulture),
            ["inStock"] = (product.StockQuantity > 0).ToString().ToLowerInvariant(),
            ["isActive"] = product.IsActive.ToString().ToLowerInvariant()
        };

        switch (product.Specifications)
        {
            case CpuSpecification cpu:
                if (Enum.IsDefined(typeof(CpuSocket), cpu.Socket))
                    facts["socket"] = cpu.Socket.ToString();
                if (cpu.CoreCount > 0)
                    facts["coreCount"] = cpu.CoreCount.ToString(CultureInfo.InvariantCulture);
                if (cpu.ThreadCount > 0)
                    facts["threadCount"] = cpu.ThreadCount.ToString(CultureInfo.InvariantCulture);
                if (cpu.BaseClockGhz > 0)
                    facts["baseClockGhz"] = cpu.BaseClockGhz.ToString(CultureInfo.InvariantCulture);
                if (cpu.BoostClockGhz > 0)
                    facts["boostClockGhz"] = cpu.BoostClockGhz.ToString(CultureInfo.InvariantCulture);
                if (cpu.TdpWattage > 0)
                    facts["tdpWattage"] = $"{cpu.TdpWattage}W";
                facts["hasIntegratedGpu"] = cpu.HasIntegratedGpu.ToString().ToLowerInvariant();
                if (Enum.IsDefined(typeof(RamType), cpu.SupportedMemoryType))
                    facts["supportedMemoryType"] = cpu.SupportedMemoryType.ToString();
                if (cpu.MaxMemorySpeedMhz > 0)
                    facts["maxMemorySpeedMhz"] = $"{cpu.MaxMemorySpeedMhz}MHz";
                break;

            case MotherboardSpecification mb:
                if (Enum.IsDefined(typeof(CpuSocket), mb.Socket))
                    facts["socket"] = mb.Socket.ToString();
                if (!string.IsNullOrWhiteSpace(mb.Chipset))
                    facts["chipset"] = mb.Chipset.Trim();
                if (Enum.IsDefined(typeof(MotherboardFormFactor), mb.FormFactor))
                    facts["formFactor"] = mb.FormFactor.ToString();
                if (Enum.IsDefined(typeof(RamType), mb.MemoryType))
                    facts["memoryType"] = mb.MemoryType.ToString();
                if (mb.MemorySlots > 0)
                    facts["memorySlots"] = mb.MemorySlots.ToString(CultureInfo.InvariantCulture);
                if (mb.MaxMemoryCapacityGb > 0)
                    facts["maxMemoryCapacityGb"] = $"{mb.MaxMemoryCapacityGb}GB";
                if (mb.M2Slots >= 0)
                    facts["m2Slots"] = mb.M2Slots.ToString(CultureInfo.InvariantCulture);
                if (mb.SataPorts >= 0)
                    facts["sataPorts"] = mb.SataPorts.ToString(CultureInfo.InvariantCulture);
                if (mb.PcieSlots > 0)
                    facts["pcieSlots"] = mb.PcieSlots.ToString(CultureInfo.InvariantCulture);
                facts["hasWifi"] = mb.HasWifi.ToString().ToLowerInvariant();
                break;

            case GpuSpecification gpu:
                if (!string.IsNullOrWhiteSpace(gpu.Chipset))
                    facts["chipset"] = gpu.Chipset.Trim();
                if (gpu.VramGb > 0)
                    facts["vramGb"] = $"{gpu.VramGb}GB";
                if (!string.IsNullOrWhiteSpace(gpu.VramType))
                    facts["vramType"] = gpu.VramType.Trim();
                if (gpu.LengthMm > 0)
                    facts["lengthMm"] = $"{gpu.LengthMm}mm";
                if (gpu.SlotWidth > 0)
                    facts["slotWidth"] = gpu.SlotWidth.ToString(CultureInfo.InvariantCulture);
                if (gpu.TdpWattage > 0)
                    facts["tdpWattage"] = $"{gpu.TdpWattage}W";
                if (gpu.RecommendedPsuWattage > 0)
                    facts["recommendedPsuWattage"] = $"{gpu.RecommendedPsuWattage}W";
                facts["requires12VHPWR"] = gpu.Requires12VHPWR.ToString().ToLowerInvariant();
                if (!string.IsNullOrWhiteSpace(gpu.PowerConnectors))
                    facts["powerConnectors"] = gpu.PowerConnectors.Trim();
                break;

            case RamSpecification ram:
                if (Enum.IsDefined(typeof(RamType), ram.MemoryType))
                    facts["memoryType"] = ram.MemoryType.ToString();
                if (Enum.IsDefined(typeof(RamFormFactor), ram.FormFactor))
                    facts["formFactor"] = ram.FormFactor.ToString();
                if (ram.CapacityGb > 0)
                    facts["capacityGb"] = $"{ram.CapacityGb}GB";
                if (ram.ModuleCount > 0)
                    facts["moduleCount"] = ram.ModuleCount.ToString(CultureInfo.InvariantCulture);
                if (ram.SpeedMhz > 0)
                    facts["speedMhz"] = $"{ram.SpeedMhz}MHz";
                if (ram.CasLatency > 0)
                    facts["casLatency"] = $"CL{ram.CasLatency}";
                facts["hasRgb"] = ram.HasRgb.ToString().ToLowerInvariant();
                break;

            case StorageSpecification storage:
                if (Enum.IsDefined(typeof(StorageType), storage.StorageType))
                    facts["storageType"] = storage.StorageType.ToString();
                if (Enum.IsDefined(typeof(StorageInterface), storage.Interface))
                    facts["interface"] = storage.Interface.ToString();
                if (storage.CapacityGb > 0)
                    facts["capacityGb"] = $"{storage.CapacityGb}GB";
                if (storage.ReadSpeedMBs > 0)
                    facts["readSpeedMBs"] = $"{storage.ReadSpeedMBs}MB/s";
                if (storage.WriteSpeedMBs > 0)
                    facts["writeSpeedMBs"] = $"{storage.WriteSpeedMBs}MB/s";
                if (!string.IsNullOrWhiteSpace(storage.FormFactor))
                    facts["formFactor"] = storage.FormFactor.Trim();
                break;

            case PsuSpecification psu:
                if (psu.Wattage > 0)
                    facts["wattage"] = $"{psu.Wattage}W";
                if (Enum.IsDefined(typeof(PsuEfficiency), psu.Efficiency))
                    facts["efficiency"] = psu.Efficiency.ToString();
                if (Enum.IsDefined(typeof(PsuModularity), psu.Modularity))
                    facts["modularity"] = psu.Modularity.ToString();
                if (Enum.IsDefined(typeof(PsuFormFactor), psu.FormFactor))
                    facts["formFactor"] = psu.FormFactor.ToString();
                facts["has12VHPWR"] = psu.Has12VHPWR.ToString().ToLowerInvariant();
                if (psu.LengthMm > 0)
                    facts["lengthMm"] = $"{psu.LengthMm}mm";
                break;

            case CaseSpecification pcCase:
                if (pcCase.SupportedFormFactors is { Count: > 0 })
                    facts["supportedFormFactors"] = string.Join(", ", pcCase.SupportedFormFactors);
                if (pcCase.MaxGpuLengthMm > 0)
                    facts["maxGpuLengthMm"] = $"{pcCase.MaxGpuLengthMm}mm";
                if (pcCase.MaxCpuCoolerHeightMm > 0)
                    facts["maxCpuCoolerHeightMm"] = $"{pcCase.MaxCpuCoolerHeightMm}mm";
                if (pcCase.MaxPsuLengthMm > 0)
                    facts["maxPsuLengthMm"] = $"{pcCase.MaxPsuLengthMm}mm";
                if (pcCase.SupportedRadiatorSizesMm is { Count: > 0 })
                    facts["supportedRadiatorSizesMm"] = string.Join(", ", pcCase.SupportedRadiatorSizesMm.Select(s => $"{s}mm"));
                if (pcCase.IncludedFans >= 0)
                    facts["includedFans"] = pcCase.IncludedFans.ToString(CultureInfo.InvariantCulture);
                break;

            case CoolerSpecification cooler:
                if (Enum.IsDefined(typeof(CoolerType), cooler.CoolerType))
                    facts["coolerType"] = cooler.CoolerType.ToString();
                if (cooler.SupportedSockets is { Count: > 0 })
                    facts["supportedSockets"] = string.Join(", ", cooler.SupportedSockets);
                if (cooler.CoolerType == CoolerType.Air && cooler.HeightMm > 0)
                    facts["heightMm"] = $"{cooler.HeightMm}mm";
                if (cooler.CoolerType != CoolerType.Air && cooler.RadiatorSizeMm > 0)
                    facts["radiatorSizeMm"] = $"{cooler.RadiatorSizeMm}mm";
                if (cooler.MaxTdpRating > 0)
                    facts["maxTdpRating"] = $"{cooler.MaxTdpRating}W";
                facts["hasRgb"] = cooler.HasRgb.ToString().ToLowerInvariant();
                break;

            case AccessorySpecification acc:
                if (!string.IsNullOrWhiteSpace(acc.Details))
                    facts["details"] = acc.Details.Trim();
                break;
        }

        return facts;
    }

    public static IReadOnlyList<string> FindMissingTechnicalFacts(
        IReadOnlyList<string>? requiredFacts,
        IReadOnlyDictionary<string, string> availableCatalogFacts)
    {
        if (requiredFacts == null || requiredFacts.Count == 0)
        {
            return [];
        }

        var missing = new List<string>();

        foreach (var rawFact in requiredFacts)
        {
            var fact = rawFact?.Trim();
            if (string.IsNullOrWhiteSpace(fact))
                continue;

            if (NonWebSearchableFacts.Contains(fact))
                continue;

            if (IsFactPresentInCatalog(fact, availableCatalogFacts))
                continue;

            if (!missing.Contains(fact, StringComparer.OrdinalIgnoreCase))
            {
                missing.Add(fact);
            }
        }

        return missing;
    }

    public static bool IsWebSearchableTechnicalFact(string factKey)
    {
        if (string.IsNullOrWhiteSpace(factKey))
            return false;

        return !NonWebSearchableFacts.Contains(factKey.Trim());
    }

    private static bool IsFactPresentInCatalog(string requestedFact, IReadOnlyDictionary<string, string> catalogFacts)
    {
        if (catalogFacts.ContainsKey(requestedFact))
            return true;

        foreach (var kvp in FactAliases)
        {
            if (string.Equals(kvp.Key, requestedFact, StringComparison.OrdinalIgnoreCase) ||
                kvp.Value.Contains(requestedFact, StringComparer.OrdinalIgnoreCase))
            {
                if (catalogFacts.ContainsKey(kvp.Key) || kvp.Value.Any(catalogFacts.ContainsKey))
                    return true;
            }
        }

        return false;
    }
}

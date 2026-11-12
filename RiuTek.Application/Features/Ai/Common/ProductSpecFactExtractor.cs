using System.Globalization;
using System.Text.RegularExpressions;
using RiuTek.Application.Common.Interfaces;
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

    private static readonly string[] AllowedManufacturerDomains =
    [
        "intel.com", "amd.com", "nvidia.com", "asus.com", "msi.com", "gigabyte.com",
        "asrock.com", "corsair.com", "kingston.com", "crucial.com", "samsung.com",
        "westerndigital.com", "wdc.com", "seagate.com", "coolermaster.com", "nzxt.com",
        "lian-li.com", "noctua.at", "bequiet.com", "seasonic.com", "sapphiretech.com",
        "powercolor.com", "zotac.com", "galax.com", "pny.com", "gskill.com",
        "teamgroupinc.com", "adata.com", "xpg.com", "lexar.com", "deepcool.com",
        "thermalright.com", "arctic.de", "phanteks.com", "fractal-design.com",
        "montechpc.com", "hyte.com", "fsp-group.com", "super-flower.com.tw"
    ];

    private static readonly string[] BlockedRetailOrSocialDomains =
    [
        "shopee.vn", "shopee.com", "lazada.vn", "tiki.vn", "amazon.com", "ebay.com",
        "aliexpress.com", "reddit.com", "facebook.com", "youtube.com", "tiktok.com",
        "gearvn.com", "phongvu.vn", "hacom.vn", "anphatpc.com.vn", "cellphones.com.vn",
        "memoryzone.com.vn", "tncstore.vn", "hoanghamobile.com"
    ];

    private static readonly IReadOnlyDictionary<string, string[]> BrandDomainMappings =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["intel"] = ["intel.com"],
            ["amd"] = ["amd.com"],
            ["ryzen"] = ["amd.com"],
            ["nvidia"] = ["nvidia.com"],
            ["asus"] = ["asus.com"],
            ["msi"] = ["msi.com"],
            ["gigabyte"] = ["gigabyte.com"],
            ["asrock"] = ["asrock.com"],
            ["corsair"] = ["corsair.com"],
            ["kingston"] = ["kingston.com"],
            ["crucial"] = ["crucial.com"],
            ["samsung"] = ["samsung.com"],
            ["seagate"] = ["seagate.com"],
            ["nzxt"] = ["nzxt.com"],
            ["noctua"] = ["noctua.at"],
            ["seasonic"] = ["seasonic.com"],
            ["sapphire"] = ["sapphiretech.com"],
            ["powercolor"] = ["powercolor.com"],
            ["zotac"] = ["zotac.com"],
            ["galax"] = ["galax.com"],
            ["pny"] = ["pny.com"],
            ["deepcool"] = ["deepcool.com"],
            ["thermalright"] = ["thermalright.com"]
        };

    private static readonly string[] StandaloneVariantTokens = ["XT", "XTX", "TI", "SUPER"];

    private static readonly Dictionary<string, string[]> FactAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["socket"] = ["socket", "cpu_socket", "supportedSockets"],
        ["coreCount"] = ["cores", "coreCount", "so_nhan"],
        ["threadCount"] = ["threads", "threadCount", "so_luong"],
        ["baseClockGhz"] = ["baseClock", "baseClockGhz", "xung_co_ban"],
        ["boostClockGhz"] = ["boostClock", "boostClockGhz", "xung_boost"],
        ["tdpWattage"] = ["tdp", "tdpWattage", "maxTdpRating", "cong_suat_toa_nhiet"],
        ["hasIntegratedGpu"] = ["igpu", "hasIntegratedGpu", "gpu_tich_hop"],
        ["l3CacheMb"] = ["l3CacheMb", "l3Cache", "l3_cache", "cacheL3", "smartCache", "bo_nho_dem_l3"],
        ["includedCooler"] = ["includedCooler", "bundledCooler", "stockCooler", "thermalSolution", "quat_tan_nhiet_kem_theo", "tan_kem_theo"],
        ["maxTurboPowerW"] = ["maxTurboPowerW", "maxTurboPower", "maximumTurboPower", "ppt", "cong_suat_turbo_toi_da"],
        ["memoryBusWidth"] = ["memoryBusWidth", "memoryBus", "busWidth", "bus_vram"],
        ["memoryType"] = ["memoryType", "supportedMemoryType", "loai_ram"],
        ["maxMemorySpeedMhz"] = ["maxMemorySpeed", "maxMemorySpeedMhz"],
        ["chipset"] = ["chipset"],
        ["formFactor"] = ["formFactor", "supportedFormFactors", "kich_thuoc"],
        ["memorySlots"] = ["memorySlots", "khe_ram"],
        ["maxMemoryCapacityGb"] = ["maxMemoryCapacity", "maxMemoryCapacityGb"],
        ["m2Slots"] = ["m2Slots", "khe_m2"],
        ["sataPorts"] = ["sataPorts", "cong_sata"],
        ["pcieSlots"] = ["pcieSlots", "khe_pcie"],
        ["hasWifi"] = ["wifi", "hasWifi"],
        ["vramGb"] = ["vram", "vramGb", "dung_luong_vram"],
        ["vramType"] = ["vramType", "loai_vram"],
        ["lengthMm"] = ["lengthMm", "maxGpuLengthMm", "maxPsuLengthMm", "chieu_dai"],
        ["slotWidth"] = ["slotWidth", "do_day_slot"],
        ["recommendedPsuWattage"] = ["recommendedPsu", "recommendedPsuWattage", "nguon_de_nghi"],
        ["requires12VHPWR"] = ["12vhpwr", "requires12VHPWR", "has12VHPWR"],
        ["powerConnectors"] = ["powerConnectors", "dau_nguon", "cong_nguon_phu"],
        ["capacityGb"] = ["capacityGb", "dung_luong"],
        ["moduleCount"] = ["moduleCount", "so_thanh"],
        ["speedMhz"] = ["speedMhz", "bus_ram", "toc_do_ram"],
        ["casLatency"] = ["casLatency", "cl", "do_tre"],
        ["hasRgb"] = ["rgb", "hasRgb"],
        ["storageType"] = ["storageType", "loai_o_cung"],
        ["interface"] = ["interface", "storageInterface", "chuan_giao_tiep"],
        ["readSpeedMBs"] = ["readSpeed", "readSpeedMBs", "toc_do_doc"],
        ["writeSpeedMBs"] = ["writeSpeed", "writeSpeedMBs", "toc_do_ghi"],
        ["wattage"] = ["wattage", "cong_suat"],
        ["efficiency"] = ["efficiency", "psuEfficiency", "chuan_80_plus"],
        ["modularity"] = ["modularity", "psuModularity", "full_modular"],
        ["maxCpuCoolerHeightMm"] = ["maxCoolerHeight", "maxCpuCoolerHeightMm", "heightMm", "chieu_cao_tan"],
        ["radiatorSizeMm"] = ["radiatorSize", "radiatorSizeMm", "supportedRadiatorSizesMm", "kich_thuoc_rad"],
        ["includedFans"] = ["includedFans", "quat_kem_theo"],
        ["coolerType"] = ["coolerType", "loai_tan"]
    };

    public static string NormalizeFactKey(string? rawFact)
    {
        if (string.IsNullOrWhiteSpace(rawFact))
        {
            return string.Empty;
        }

        var trimmed = rawFact.Trim();
        foreach (var kvp in FactAliases)
        {
            if (string.Equals(kvp.Key, trimmed, StringComparison.OrdinalIgnoreCase) ||
                kvp.Value.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            {
                return kvp.Key;
            }
        }

        return trimmed;
    }

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
                if (cpu.L3CacheMb > 0)
                    facts["l3CacheMb"] = $"{cpu.L3CacheMb}MB";
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

            if (NonWebSearchableFacts.Contains(fact) || CuratedFaqRepository.IsPolicyFactKey(fact))
                continue;

            var normalized = NormalizeFactKey(fact);
            if (NonWebSearchableFacts.Contains(normalized) || CuratedFaqRepository.IsPolicyFactKey(normalized))
                continue;

            if (IsFactPresentInCatalog(fact, availableCatalogFacts) ||
                IsFactPresentInCatalog(normalized, availableCatalogFacts))
                continue;

            if (!missing.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                missing.Add(normalized);
            }
        }

        return missing;
    }

    public static bool IsWebSearchableTechnicalFact(string factKey)
    {
        if (string.IsNullOrWhiteSpace(factKey))
            return false;

        var trimmed = factKey.Trim();
        var normalized = NormalizeFactKey(trimmed);

        return !NonWebSearchableFacts.Contains(trimmed) &&
               !NonWebSearchableFacts.Contains(normalized) &&
               !CuratedFaqRepository.IsPolicyFactKey(trimmed) &&
               !CuratedFaqRepository.IsPolicyFactKey(normalized);
    }

    public static bool IsFactPresentInCatalog(string requestedFact, IReadOnlyDictionary<string, string> catalogFacts)
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

    public static bool IsTrustedManufacturerUrl(string? rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl) ||
            !Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var host = uri.Host.ToLowerInvariant();

        if (BlockedRetailOrSocialDomains.Any(blocked =>
            string.Equals(host, blocked, StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith("." + blocked, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return AllowedManufacturerDomains.Any(allowed =>
            string.Equals(host, allowed, StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith("." + allowed, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsTrustedManufacturerUrlForModel(string? rawUrl, string brandAndModel)
    {
        if (!IsTrustedManufacturerUrl(rawUrl) || !Uri.TryCreate(rawUrl!.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.Host.ToLowerInvariant();
        var tokens = brandAndModel
            .Split([' ', '-', '_', '/', '(', ')'], StringSplitOptions.RemoveEmptyEntries);

        var expectedDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in tokens)
        {
            if (BrandDomainMappings.TryGetValue(token, out var domains))
            {
                foreach (var d in domains)
                {
                    expectedDomains.Add(d);
                }
            }
        }

        if (expectedDomains.Count == 0)
        {
            return true;
        }

        return expectedDomains.Any(allowed =>
            string.Equals(host, allowed, StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith("." + allowed, StringComparison.OrdinalIgnoreCase));
    }

    public static bool DoesTextMatchExactModel(string text, string brandAndModel)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(brandAndModel))
        {
            return false;
        }

        var rawTokens = brandAndModel
            .Split([' ', '-', '_', '/', '(', ')'], StringSplitOptions.RemoveEmptyEntries);

        var modelTokens = rawTokens
            .Where(tok => tok.Length >= 2 && tok.Any(char.IsDigit))
            .ToList();

        if (modelTokens.Count == 0)
        {
            return false;
        }

        foreach (var tok in modelTokens)
        {
            var pattern = $@"(?<![A-Za-z0-9]){Regex.Escape(tok)}(?![A-Za-z0-9])";
            if (!Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return false;
            }
        }

        var targetStandaloneVariants = rawTokens
            .Where(t => StandaloneVariantTokens.Contains(t, StringComparer.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var requiredVariant in targetStandaloneVariants)
        {
            var reqPattern = $@"(?<![A-Za-z0-9]){Regex.Escape(requiredVariant)}(?![A-Za-z0-9])";
            if (!Regex.IsMatch(text, reqPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return false;
            }
        }

        var primaryNumericToken = modelTokens.OrderByDescending(t => t.Length).First();
        foreach (var variant in StandaloneVariantTokens)
        {
            if (targetStandaloneVariants.Contains(variant))
            {
                continue;
            }

            var forbiddenSpacedVariantPattern =
                $@"(?<![A-Za-z0-9]){Regex.Escape(primaryNumericToken)}[\s\-_]+{Regex.Escape(variant)}(?![A-Za-z0-9])";

            if (Regex.IsMatch(text, forbiddenSpacedVariantPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return false;
            }
        }

        return true;
    }

    public static bool TryVerifyFactInSnippet(string normalizedFactKey, string snippet, out string extractedSignature)
    {
        extractedSignature = string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedFactKey) || string.IsNullOrWhiteSpace(snippet))
        {
            return false;
        }

        var key = NormalizeFactKey(normalizedFactKey);

        switch (key.ToLowerInvariant())
        {
            case "l3cachemb":
            {
                var hasL3Keyword = Regex.IsMatch(
                    snippet,
                    @"(\bL3\b|Smart\s*Cache|Cache\s*L3)",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                var valMatch = Regex.Match(
                    snippet,
                    @"\b(\d+(?:\.\d+)?)\s*MB\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

                if (hasL3Keyword && valMatch.Success)
                {
                    extractedSignature = $"{valMatch.Groups[1].Value}MB";
                    return true;
                }
                return false;
            }

            case "includedcooler":
            {
                var coolerMatch = Regex.Match(
                    snippet,
                    @"(Wraith\s*[A-Za-z0-9\s]*|Thermal\s+Solution[^\.,;]*|Included\s+Cooler[^\.,;]*|Boxed\s+Cooler[^\.,;]*|Cooler\s+Included[^\.,;]*|No\s+Cooler|Without\s+Cooler|Not\s+Included)",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (coolerMatch.Success)
                {
                    extractedSignature = coolerMatch.Value.Trim().ToUpperInvariant();
                    return true;
                }
                return false;
            }

            case "tdpwattage":
            {
                var hasTdpKeyword = Regex.IsMatch(
                    snippet,
                    @"(\bTDP\b|Processor\s+Base\s+Power|Base\s+Power|Default\s+TDP|Board\s+Power|Total\s+Graphics\s+Power|\bTGP\b|\bTBP\b)",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                var valMatch = Regex.Match(
                    snippet,
                    @"\b(\d+)\s*W\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

                if (hasTdpKeyword && valMatch.Success)
                {
                    extractedSignature = $"{valMatch.Groups[1].Value}W";
                    return true;
                }
                return false;
            }

            case "maxturbopowerw":
            {
                var hasTurboKeyword = Regex.IsMatch(
                    snippet,
                    @"(Maximum\s+Turbo\s+Power|Max\s+Turbo\s+Power|Turbo\s+Power|\bPPT\b|Max\s+Power)",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                var matches = Regex.Matches(
                    snippet,
                    @"\b(\d+)\s*W\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

                if (hasTurboKeyword && matches.Count > 0)
                {
                    extractedSignature = $"{matches[^1].Groups[1].Value}W";
                    return true;
                }
                return false;
            }

            case "memorybuswidth":
            {
                var hasBusKeyword = Regex.IsMatch(
                    snippet,
                    @"(Memory\s+Bus|Bus\s+Width|Memory\s+Interface)",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                var valMatch = Regex.Match(
                    snippet,
                    @"\b(\d+)\s*-?\s*bit\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

                if (hasBusKeyword && valMatch.Success)
                {
                    extractedSignature = $"{valMatch.Groups[1].Value}-bit";
                    return true;
                }
                return false;
            }

            case "socket":
            {
                var sockMatch = Regex.Match(
                    snippet,
                    @"\b(LGA\s*\d+|AM4|AM5|sTR5|sTRX4)\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (sockMatch.Success)
                {
                    extractedSignature = sockMatch.Groups[1].Value.Replace(" ", "").ToUpperInvariant();
                    return true;
                }
                return false;
            }

            case "corecount":
            {
                var match = Regex.Match(
                    snippet,
                    @"(?:(?:\b(\d+)\s*(?:Cores?|nhân)\b)|(?:(?:#\s*of\s*CPU\s*Cores|Total\s+Cores)[\s:]*(\d+)))",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (match.Success)
                {
                    extractedSignature = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
                    return true;
                }
                return false;
            }

            case "threadcount":
            {
                var match = Regex.Match(
                    snippet,
                    @"(?:(?:\b(\d+)\s*(?:Threads?|luồng)\b)|(?:(?:#\s*of\s*Threads|Total\s+Threads)[\s:]*(\d+)))",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (match.Success)
                {
                    extractedSignature = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
                    return true;
                }
                return false;
            }

            case "boostclockghz":
            case "baseclockghz":
            {
                var match = Regex.Match(
                    snippet,
                    @"\b(\d+(?:\.\d+)?)\s*GHz\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (match.Success)
                {
                    extractedSignature = $"{match.Groups[1].Value}GHz";
                    return true;
                }
                return false;
            }

            case "vramgb":
            {
                var match = Regex.Match(
                    snippet,
                    @"\b(\d+)\s*GB\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (match.Success)
                {
                    extractedSignature = $"{match.Groups[1].Value}GB";
                    return true;
                }
                return false;
            }

            case "lengthmm":
            {
                var match = Regex.Match(
                    snippet,
                    @"\b(\d+)\s*mm\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (match.Success)
                {
                    extractedSignature = $"{match.Groups[1].Value}mm";
                    return true;
                }
                return false;
            }

            case "recommendedpsuwattage":
            case "wattage":
            {
                var match = Regex.Match(
                    snippet,
                    @"\b(\d+)\s*W\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (match.Success)
                {
                    extractedSignature = $"{match.Groups[1].Value}W";
                    return true;
                }
                return false;
            }

            case "maxmemoryspeedmhz":
            case "speedmhz":
            {
                var match = Regex.Match(
                    snippet,
                    @"\b(\d{4,5})\s*(?:MHz|MT/s)\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (match.Success)
                {
                    extractedSignature = $"{match.Groups[1].Value}MHz";
                    return true;
                }
                return false;
            }

            default:
                return false;
        }
    }

    public static bool IsEvidenceValidForTarget(
        GroqWebSearchResult evidence,
        string targetBrandAndModel,
        string targetProductKey,
        IReadOnlySet<string> targetMissingFacts)
    {
        if (evidence == null ||
            string.IsNullOrWhiteSpace(evidence.SourceUrl) ||
            string.IsNullOrWhiteSpace(evidence.Summary) ||
            string.IsNullOrWhiteSpace(evidence.FactKey))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(evidence.ProductKey) &&
            !string.Equals(evidence.ProductKey, targetProductKey, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var normalizedFactKey = NormalizeFactKey(evidence.FactKey);
        if (!targetMissingFacts.Contains(normalizedFactKey) && !targetMissingFacts.Contains(evidence.FactKey))
        {
            return false;
        }

        if (!IsTrustedManufacturerUrlForModel(evidence.SourceUrl, targetBrandAndModel))
        {
            return false;
        }

        var combinedSourceText = $"{evidence.SourceTitle} {evidence.Summary} {evidence.SourceUrl}";
        if (!DoesTextMatchExactModel(combinedSourceText, targetBrandAndModel))
        {
            return false;
        }

        var snippetText = $"{evidence.SourceTitle}. {evidence.Summary}";
        return TryVerifyFactInSnippet(normalizedFactKey, snippetText, out _);
    }
}

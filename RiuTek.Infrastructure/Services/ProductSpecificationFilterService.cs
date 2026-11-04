using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Products.Queries;
using RiuTek.Core.Entities;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;

namespace RiuTek.Infrastructure.Services;

public class ProductSpecificationFilterService : IProductSpecificationFilterService
{
    public IQueryable<Product> Apply(IQueryable<Product> query, ProductFilterOptions options)
    {
        if (!options.ComponentType.HasValue)
        {
            return query;
        }

        var componentType = options.ComponentType.Value;

        // 1. Sockets: Cpu, Motherboard (Socket), Cooler (SupportedSockets contains value)
        var sockets = (options.Sockets ?? []).ToList();
        if (options.Socket.HasValue && !sockets.Contains(options.Socket.Value))
        {
            sockets.Add(options.Socket.Value);
        }
        sockets = sockets.Distinct().ToList();

        if (sockets.Count > 0)
        {
            if (componentType is ComponentType.Cpu or ComponentType.Motherboard)
            {
                var jsonSnippets = sockets.Select(s => CreateScalarJson(nameof(CpuSpecification.Socket), (int)s)).ToList();
                query = ApplyOrJsonContains(query, jsonSnippets);
            }
            else if (componentType is ComponentType.Cooler)
            {
                var jsonSnippets = sockets.Select(s => CreateArrayElementJson(nameof(CoolerSpecification.SupportedSockets), (int)s)).ToList();
                query = ApplyOrJsonContains(query, jsonSnippets);
            }
        }

        // 2. MemoryTypes: Cpu (SupportedMemoryType), Motherboard / Ram (MemoryType)
        var memoryTypes = (options.MemoryTypes ?? []).ToList();
        if (options.MemoryType.HasValue && !memoryTypes.Contains(options.MemoryType.Value))
        {
            memoryTypes.Add(options.MemoryType.Value);
        }
        memoryTypes = memoryTypes.Distinct().ToList();

        if (memoryTypes.Count > 0)
        {
            if (componentType is ComponentType.Cpu)
            {
                var jsonSnippets = memoryTypes.Select(m => CreateScalarJson(nameof(CpuSpecification.SupportedMemoryType), (int)m)).ToList();
                query = ApplyOrJsonContains(query, jsonSnippets);
            }
            else if (componentType is ComponentType.Motherboard or ComponentType.Ram)
            {
                var jsonSnippets = memoryTypes.Select(m => CreateScalarJson(nameof(MotherboardSpecification.MemoryType), (int)m)).ToList();
                query = ApplyOrJsonContains(query, jsonSnippets);
            }
        }

        // 3. MotherboardFormFactors: Motherboard (FormFactor), Case (SupportedFormFactors contains value)
        var mbFormFactors = (options.MotherboardFormFactors ?? []).ToList();
        if (options.MotherboardFormFactor.HasValue && !mbFormFactors.Contains(options.MotherboardFormFactor.Value))
        {
            mbFormFactors.Add(options.MotherboardFormFactor.Value);
        }
        mbFormFactors = mbFormFactors.Distinct().ToList();

        if (mbFormFactors.Count > 0)
        {
            if (componentType is ComponentType.Motherboard)
            {
                var jsonSnippets = mbFormFactors.Select(f => CreateScalarJson(nameof(MotherboardSpecification.FormFactor), (int)f)).ToList();
                query = ApplyOrJsonContains(query, jsonSnippets);
            }
            else if (componentType is ComponentType.Case)
            {
                var jsonSnippets = mbFormFactors.Select(f => CreateArrayElementJson(nameof(CaseSpecification.SupportedFormFactors), (int)f)).ToList();
                query = ApplyOrJsonContains(query, jsonSnippets);
            }
        }

        // 4. RamFormFactors: Ram (FormFactor)
        var ramFormFactors = (options.RamFormFactors ?? []).ToList();
        if (options.RamFormFactor.HasValue && !ramFormFactors.Contains(options.RamFormFactor.Value))
        {
            ramFormFactors.Add(options.RamFormFactor.Value);
        }
        ramFormFactors = ramFormFactors.Distinct().ToList();

        if (ramFormFactors.Count > 0 && componentType is ComponentType.Ram)
        {
            var jsonSnippets = ramFormFactors.Select(f => CreateScalarJson(nameof(RamSpecification.FormFactor), (int)f)).ToList();
            query = ApplyOrJsonContains(query, jsonSnippets);
        }

        // 5. StorageTypes: Storage (StorageType)
        var storageTypes = (options.StorageTypes ?? []).ToList();
        if (options.StorageType.HasValue && !storageTypes.Contains(options.StorageType.Value))
        {
            storageTypes.Add(options.StorageType.Value);
        }
        storageTypes = storageTypes.Distinct().ToList();

        if (storageTypes.Count > 0 && componentType is ComponentType.Storage)
        {
            var jsonSnippets = storageTypes.Select(t => CreateScalarJson(nameof(StorageSpecification.StorageType), (int)t)).ToList();
            query = ApplyOrJsonContains(query, jsonSnippets);
        }

        // 6. StorageInterfaces: Storage (Interface)
        var storageInterfaces = (options.StorageInterfaces ?? []).ToList();
        if (options.StorageInterface.HasValue && !storageInterfaces.Contains(options.StorageInterface.Value))
        {
            storageInterfaces.Add(options.StorageInterface.Value);
        }
        storageInterfaces = storageInterfaces.Distinct().ToList();

        if (storageInterfaces.Count > 0 && componentType is ComponentType.Storage)
        {
            var jsonSnippets = storageInterfaces.Select(i => CreateScalarJson(nameof(StorageSpecification.Interface), (int)i)).ToList();
            query = ApplyOrJsonContains(query, jsonSnippets);
        }

        // 7. PsuEfficiencies: Psu (Efficiency)
        var psuEfficiencies = (options.PsuEfficiencies ?? []).ToList();
        if (options.PsuEfficiency.HasValue && !psuEfficiencies.Contains(options.PsuEfficiency.Value))
        {
            psuEfficiencies.Add(options.PsuEfficiency.Value);
        }
        psuEfficiencies = psuEfficiencies.Distinct().ToList();

        if (psuEfficiencies.Count > 0 && componentType is ComponentType.Psu)
        {
            var jsonSnippets = psuEfficiencies.Select(e => CreateScalarJson(nameof(PsuSpecification.Efficiency), (int)e)).ToList();
            query = ApplyOrJsonContains(query, jsonSnippets);
        }

        // 8. PsuModularities: Psu (Modularity)
        var psuModularities = (options.PsuModularities ?? []).ToList();
        if (options.PsuModularity.HasValue && !psuModularities.Contains(options.PsuModularity.Value))
        {
            psuModularities.Add(options.PsuModularity.Value);
        }
        psuModularities = psuModularities.Distinct().ToList();

        if (psuModularities.Count > 0 && componentType is ComponentType.Psu)
        {
            var jsonSnippets = psuModularities.Select(m => CreateScalarJson(nameof(PsuSpecification.Modularity), (int)m)).ToList();
            query = ApplyOrJsonContains(query, jsonSnippets);
        }

        // 9. PsuFormFactors: Psu (FormFactor)
        var psuFormFactors = (options.PsuFormFactors ?? []).ToList();
        if (options.PsuFormFactor.HasValue && !psuFormFactors.Contains(options.PsuFormFactor.Value))
        {
            psuFormFactors.Add(options.PsuFormFactor.Value);
        }
        psuFormFactors = psuFormFactors.Distinct().ToList();

        if (psuFormFactors.Count > 0 && componentType is ComponentType.Psu)
        {
            var jsonSnippets = psuFormFactors.Select(f => CreateScalarJson(nameof(PsuSpecification.FormFactor), (int)f)).ToList();
            query = ApplyOrJsonContains(query, jsonSnippets);
        }

        // 10. CoolerTypes: Cooler (CoolerType)
        var coolerTypes = (options.CoolerTypes ?? []).ToList();
        if (options.CoolerType.HasValue && !coolerTypes.Contains(options.CoolerType.Value))
        {
            coolerTypes.Add(options.CoolerType.Value);
        }
        coolerTypes = coolerTypes.Distinct().ToList();

        if (coolerTypes.Count > 0 && componentType is ComponentType.Cooler)
        {
            var jsonSnippets = coolerTypes.Select(c => CreateScalarJson(nameof(CoolerSpecification.CoolerType), (int)c)).ToList();
            query = ApplyOrJsonContains(query, jsonSnippets);
        }

        // 11. CoreCounts: Cpu (CoreCount)
        if (options.CoreCounts != null && componentType is ComponentType.Cpu)
        {
            var coreCounts = options.CoreCounts.Where(c => c > 0).Distinct().ToList();
            if (coreCounts.Count > 0)
            {
                var jsonSnippets = coreCounts.Select(c => CreateScalarJson(nameof(CpuSpecification.CoreCount), c)).ToList();
                query = ApplyOrJsonContains(query, jsonSnippets);
            }
        }

        // 12. VramGbs: Gpu (VramGb)
        if (options.VramGbs != null && componentType is ComponentType.Gpu)
        {
            var vramGbs = options.VramGbs.Where(v => v > 0).Distinct().ToList();
            if (vramGbs.Count > 0)
            {
                var jsonSnippets = vramGbs.Select(v => CreateScalarJson(nameof(GpuSpecification.VramGb), v)).ToList();
                query = ApplyOrJsonContains(query, jsonSnippets);
            }
        }

        // 13. GpuLengthsMm: Gpu (LengthMm)
        if (options.GpuLengthsMm != null && componentType is ComponentType.Gpu)
        {
            var gpuLengths = options.GpuLengthsMm.Where(l => l > 0).Distinct().ToList();
            if (gpuLengths.Count > 0)
            {
                var jsonSnippets = gpuLengths.Select(l => CreateScalarJson(nameof(GpuSpecification.LengthMm), l)).ToList();
                query = ApplyOrJsonContains(query, jsonSnippets);
            }
        }

        // 14. RamCapacitiesGb: Ram (CapacityGb - total kit)
        if (options.RamCapacitiesGb != null && componentType is ComponentType.Ram)
        {
            var ramCapacities = options.RamCapacitiesGb.Where(c => c > 0).Distinct().ToList();
            if (ramCapacities.Count > 0)
            {
                var jsonSnippets = ramCapacities.Select(c => CreateScalarJson(nameof(RamSpecification.CapacityGb), c)).ToList();
                query = ApplyOrJsonContains(query, jsonSnippets);
            }
        }

        // 15. RamSpeedsMhz: Ram (SpeedMhz)
        if (options.RamSpeedsMhz != null && componentType is ComponentType.Ram)
        {
            var ramSpeeds = options.RamSpeedsMhz.Where(s => s > 0).Distinct().ToList();
            if (ramSpeeds.Count > 0)
            {
                var jsonSnippets = ramSpeeds.Select(s => CreateScalarJson(nameof(RamSpecification.SpeedMhz), s)).ToList();
                query = ApplyOrJsonContains(query, jsonSnippets);
            }
        }

        // 16. StorageCapacitiesGb: Storage (CapacityGb)
        if (options.StorageCapacitiesGb != null && componentType is ComponentType.Storage)
        {
            var storageCapacities = options.StorageCapacitiesGb.Where(c => c > 0).Distinct().ToList();
            if (storageCapacities.Count > 0)
            {
                var jsonSnippets = storageCapacities.Select(c => CreateScalarJson(nameof(StorageSpecification.CapacityGb), c)).ToList();
                query = ApplyOrJsonContains(query, jsonSnippets);
            }
        }

        // 17. PsuWattages: Psu (Wattage)
        if (options.PsuWattages != null && componentType is ComponentType.Psu)
        {
            var psuWattages = options.PsuWattages.Where(w => w > 0).Distinct().ToList();
            if (psuWattages.Count > 0)
            {
                var jsonSnippets = psuWattages.Select(w => CreateScalarJson(nameof(PsuSpecification.Wattage), w)).ToList();
                query = ApplyOrJsonContains(query, jsonSnippets);
            }
        }

        // 18. HasIntegratedGpu: Cpu (HasIntegratedGpu)
        if (options.HasIntegratedGpu.HasValue && componentType is ComponentType.Cpu)
        {
            var json = CreateScalarJson(nameof(CpuSpecification.HasIntegratedGpu), options.HasIntegratedGpu.Value);
            query = query.Where(p => EF.Functions.JsonContains(p.Specifications, json));
        }

        // 19. HasWifi: Motherboard (HasWifi)
        if (options.HasWifi.HasValue && componentType is ComponentType.Motherboard)
        {
            var json = CreateScalarJson(nameof(MotherboardSpecification.HasWifi), options.HasWifi.Value);
            query = query.Where(p => EF.Functions.JsonContains(p.Specifications, json));
        }

        // 20. HasRgb: Ram / Cooler (HasRgb)
        if (options.HasRgb.HasValue && componentType is ComponentType.Ram or ComponentType.Cooler)
        {
            var json = CreateScalarJson(nameof(RamSpecification.HasRgb), options.HasRgb.Value);
            query = query.Where(p => EF.Functions.JsonContains(p.Specifications, json));
        }

        // 21. Has12VHPWR: Psu (Has12VHPWR)
        if (options.Has12VHPWR.HasValue && componentType is ComponentType.Psu)
        {
            var json = CreateScalarJson(nameof(PsuSpecification.Has12VHPWR), options.Has12VHPWR.Value);
            query = query.Where(p => EF.Functions.JsonContains(p.Specifications, json));
        }

        // 22. Requires12VHPWR: Gpu (Requires12VHPWR)
        if (options.Requires12VHPWR.HasValue && componentType is ComponentType.Gpu)
        {
            var json = CreateScalarJson(nameof(GpuSpecification.Requires12VHPWR), options.Requires12VHPWR.Value);
            query = query.Where(p => EF.Functions.JsonContains(p.Specifications, json));
        }

        return query;
    }

    public async Task<ProductFilterOptionsDto> GetFilterOptionsAsync(
        IQueryable<Product> scopedQuery,
        ComponentType? componentType,
        CancellationToken cancellationToken = default)
    {
        var rawBrands = await scopedQuery
            .Select(p => p.Brand)
            .Distinct()
            .ToListAsync(cancellationToken);

        var brands = rawBrands
            .Where(b => !string.IsNullOrWhiteSpace(b))
            .Select(b => b.Trim())
            .GroupBy(b => b.ToUpperInvariant())
            .Select(g => g.First())
            .OrderBy(b => b, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!componentType.HasValue)
        {
            return new ProductFilterOptionsDto(brands, []);
        }

        var specs = await scopedQuery
            .Select(p => p.Specifications)
            .ToListAsync(cancellationToken);

        var groups = new List<ProductFilterGroupDto>();

        switch (componentType.Value)
        {
            case ComponentType.Cpu:
            {
                var cpuSpecs = specs.OfType<CpuSpecification>().ToList();
                groups.Add(new ProductFilterGroupDto("sockets", cpuSpecs.Select(s => (int)s.Socket).Distinct().OrderBy(x => x).ToList()));
                groups.Add(new ProductFilterGroupDto("memoryTypes", cpuSpecs.Select(s => (int)s.SupportedMemoryType).Distinct().OrderBy(x => x).ToList()));
                groups.Add(new ProductFilterGroupDto("coreCounts", cpuSpecs.Where(s => s.CoreCount > 0).Select(s => s.CoreCount).Distinct().OrderBy(x => x).ToList()));
                break;
            }
            case ComponentType.Motherboard:
            {
                var mbSpecs = specs.OfType<MotherboardSpecification>().ToList();
                groups.Add(new ProductFilterGroupDto("sockets", mbSpecs.Select(s => (int)s.Socket).Distinct().OrderBy(x => x).ToList()));
                groups.Add(new ProductFilterGroupDto("memoryTypes", mbSpecs.Select(s => (int)s.MemoryType).Distinct().OrderBy(x => x).ToList()));
                groups.Add(new ProductFilterGroupDto("motherboardFormFactors", mbSpecs.Select(s => (int)s.FormFactor).Distinct().OrderBy(x => x).ToList()));
                break;
            }
            case ComponentType.Gpu:
            {
                var gpuSpecs = specs.OfType<GpuSpecification>().ToList();
                groups.Add(new ProductFilterGroupDto("vramGbs", gpuSpecs.Where(s => s.VramGb > 0).Select(s => s.VramGb).Distinct().OrderBy(x => x).ToList()));
                groups.Add(new ProductFilterGroupDto("gpuLengthsMm", gpuSpecs.Where(s => s.LengthMm > 0).Select(s => s.LengthMm).Distinct().OrderBy(x => x).ToList()));
                break;
            }
            case ComponentType.Ram:
            {
                var ramSpecs = specs.OfType<RamSpecification>().ToList();
                groups.Add(new ProductFilterGroupDto("memoryTypes", ramSpecs.Select(s => (int)s.MemoryType).Distinct().OrderBy(x => x).ToList()));
                groups.Add(new ProductFilterGroupDto("ramFormFactors", ramSpecs.Select(s => (int)s.FormFactor).Distinct().OrderBy(x => x).ToList()));
                groups.Add(new ProductFilterGroupDto("ramCapacitiesGb", ramSpecs.Where(s => s.CapacityGb > 0).Select(s => s.CapacityGb).Distinct().OrderBy(x => x).ToList()));
                groups.Add(new ProductFilterGroupDto("ramSpeedsMhz", ramSpecs.Where(s => s.SpeedMhz > 0).Select(s => s.SpeedMhz).Distinct().OrderBy(x => x).ToList()));
                break;
            }
            case ComponentType.Storage:
            {
                var storageSpecs = specs.OfType<StorageSpecification>().ToList();
                groups.Add(new ProductFilterGroupDto("storageTypes", storageSpecs.Select(s => (int)s.StorageType).Distinct().OrderBy(x => x).ToList()));
                groups.Add(new ProductFilterGroupDto("storageInterfaces", storageSpecs.Select(s => (int)s.Interface).Distinct().OrderBy(x => x).ToList()));
                groups.Add(new ProductFilterGroupDto("storageCapacitiesGb", storageSpecs.Where(s => s.CapacityGb > 0).Select(s => s.CapacityGb).Distinct().OrderBy(x => x).ToList()));
                break;
            }
            case ComponentType.Psu:
            {
                var psuSpecs = specs.OfType<PsuSpecification>().ToList();
                groups.Add(new ProductFilterGroupDto("psuEfficiencies", psuSpecs.Select(s => (int)s.Efficiency).Distinct().OrderBy(x => x).ToList()));
                groups.Add(new ProductFilterGroupDto("psuModularities", psuSpecs.Select(s => (int)s.Modularity).Distinct().OrderBy(x => x).ToList()));
                groups.Add(new ProductFilterGroupDto("psuFormFactors", psuSpecs.Select(s => (int)s.FormFactor).Distinct().OrderBy(x => x).ToList()));
                groups.Add(new ProductFilterGroupDto("psuWattages", psuSpecs.Where(s => s.Wattage > 0).Select(s => s.Wattage).Distinct().OrderBy(x => x).ToList()));
                break;
            }
            case ComponentType.Case:
            {
                var caseSpecs = specs.OfType<CaseSpecification>().ToList();
                groups.Add(new ProductFilterGroupDto("motherboardFormFactors", caseSpecs.SelectMany(s => s.SupportedFormFactors).Select(f => (int)f).Distinct().OrderBy(x => x).ToList()));
                break;
            }
            case ComponentType.Cooler:
            {
                var coolerSpecs = specs.OfType<CoolerSpecification>().ToList();
                groups.Add(new ProductFilterGroupDto("sockets", coolerSpecs.SelectMany(s => s.SupportedSockets).Select(s => (int)s).Distinct().OrderBy(x => x).ToList()));
                groups.Add(new ProductFilterGroupDto("coolerTypes", coolerSpecs.Select(s => (int)s.CoolerType).Distinct().OrderBy(x => x).ToList()));
                break;
            }
            case ComponentType.Accessory:
            default:
                break;
        }

        return new ProductFilterOptionsDto(brands, groups);
    }

    private static IQueryable<Product> ApplyOrJsonContains(
        IQueryable<Product> query,
        IReadOnlyList<string> jsonSnippets)
    {
        if (jsonSnippets.Count == 0) return query;

        if (jsonSnippets.Count == 1)
        {
            var json = jsonSnippets[0];
            return query.Where(p => EF.Functions.JsonContains(p.Specifications, json));
        }

        Expression<Func<Product, bool>>? orExpr = null;
        foreach (var json in jsonSnippets)
        {
            var predicate = CreateJsonContainsPredicate(json);
            orExpr = orExpr == null ? predicate : PredicateBuilder.Or(orExpr, predicate);
        }

        return query.Where(orExpr!);
    }

    private static Expression<Func<Product, bool>> CreateJsonContainsPredicate(string json)
    {
        return p => EF.Functions.JsonContains(p.Specifications, json);
    }

    private static string CreateScalarJson(string propertyName, object value)
    {
        return JsonSerializer.Serialize(new Dictionary<string, object>
        {
            [propertyName] = value
        });
    }

    private static string CreateArrayElementJson(string propertyName, object element)
    {
        return JsonSerializer.Serialize(new Dictionary<string, object>
        {
            [propertyName] = new[] { element }
        });
    }

    private static class PredicateBuilder
    {
        public static Expression<Func<T, bool>> Or<T>(
            Expression<Func<T, bool>> expr1,
            Expression<Func<T, bool>> expr2)
        {
            var parameter = Expression.Parameter(typeof(T), "p");
            var leftVisitor = new ReplaceParameterVisitor(expr1.Parameters[0], parameter);
            var left = leftVisitor.Visit(expr1.Body);

            var rightVisitor = new ReplaceParameterVisitor(expr2.Parameters[0], parameter);
            var right = rightVisitor.Visit(expr2.Body);

            return Expression.Lambda<Func<T, bool>>(Expression.OrElse(left, right), parameter);
        }

        private sealed class ReplaceParameterVisitor : ExpressionVisitor
        {
            private readonly ParameterExpression _oldParam;
            private readonly ParameterExpression _newParam;

            public ReplaceParameterVisitor(ParameterExpression oldParam, ParameterExpression newParam)
            {
                _oldParam = oldParam;
                _newParam = newParam;
            }

            protected override Expression VisitParameter(ParameterExpression node) =>
                node == _oldParam ? _newParam : base.VisitParameter(node);
        }
    }
}

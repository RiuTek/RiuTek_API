using System.Linq.Expressions;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Products.Queries;
using RiuTek.Core.Entities;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;
using RiuTek.Infrastructure.Data;

namespace RiuTek.Infrastructure.Services;

public class ProductSpecificationFilterService : IProductSpecificationFilterService
{
    private readonly ApplicationDbContext? _context;

    public ProductSpecificationFilterService(ApplicationDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public ProductSpecificationFilterService()
    {
        _context = null;
    }
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

    private sealed record GroupDefinition(
        string Key,
        string JsonProperty,
        bool IsArray,
        Type? EnumType);

    public sealed record FilterBrandRow(string Brand);
    public sealed record FilterOptionGroupRow(string Key, int Value);

    public async Task<ProductFilterOptionsDto> GetFilterOptionsAsync(
        ProductFilterScope scope,
        CancellationToken cancellationToken = default)
    {
        var brands = await QueryBrandsAsync(scope, cancellationToken);
        var groups = await QueryGroupsAsync(scope, cancellationToken);

        return new ProductFilterOptionsDto(brands, groups);
    }

    private async Task<IReadOnlyList<string>> QueryBrandsAsync(
        ProductFilterScope scope,
        CancellationToken cancellationToken)
    {
        if (_context == null)
        {
            throw new InvalidOperationException("ApplicationDbContext is required for GetFilterOptionsAsync.");
        }

        var sqlBuilder = new StringBuilder();
        sqlBuilder.AppendLine("""
            SELECT MIN(TRIM(p."Brand") COLLATE "C") AS "Brand"
            FROM "Products" p
            WHERE p."Brand" IS NOT NULL
              AND TRIM(p."Brand") <> ''
        """);

        var parameters = new List<object>();
        AppendScopeConditions(sqlBuilder, parameters, scope);

        sqlBuilder.AppendLine("""
            GROUP BY UPPER(TRIM(p."Brand"))
            ORDER BY "Brand" ASC;
        """);

        var brandRows = await _context.Database
            .SqlQueryRaw<FilterBrandRow>(sqlBuilder.ToString(), parameters.ToArray())
            .ToListAsync(cancellationToken);

        return brandRows
            .Select(r => r.Brand)
            .Where(b => !string.IsNullOrWhiteSpace(b))
            .ToList();
    }

    private async Task<IReadOnlyList<ProductFilterGroupDto>> QueryGroupsAsync(
        ProductFilterScope scope,
        CancellationToken cancellationToken)
    {
        if (!scope.ComponentType.HasValue || _context == null)
        {
            return [];
        }

        var groupDefs = GetGroupDefinitions(scope.ComponentType.Value);
        if (groupDefs.Count == 0)
        {
            return [];
        }

        var sqlBuilder = new StringBuilder();
        sqlBuilder.AppendLine("WITH scoped_products AS (");
        sqlBuilder.AppendLine("    SELECT p.\"Specifications\"");
        sqlBuilder.AppendLine("    FROM \"Products\" p");
        sqlBuilder.AppendLine("    WHERE 1=1");

        var parameters = new List<object>();
        AppendScopeConditions(sqlBuilder, parameters, scope);

        sqlBuilder.AppendLine(")");

        for (int i = 0; i < groupDefs.Count; i++)
        {
            if (i > 0)
            {
                sqlBuilder.AppendLine("UNION");
            }

            var def = groupDefs[i];
            if (def.IsArray)
            {
                sqlBuilder.AppendLine($"""
                    SELECT '{def.Key}' AS "Key", elem::int AS "Value"
                    FROM scoped_products s,
                         jsonb_array_elements_text(s."Specifications"->'{def.JsonProperty}') AS elem
                    WHERE s."Specifications" IS NOT NULL
                      AND jsonb_typeof(s."Specifications") = 'object'
                      AND s."Specifications" ? '{def.JsonProperty}'
                      AND jsonb_typeof(s."Specifications"->'{def.JsonProperty}') = 'array'
                      AND elem ~ '^[0-9]+$'
                      AND elem::int > 0
                """);
            }
            else
            {
                sqlBuilder.AppendLine($"""
                    SELECT '{def.Key}' AS "Key", (s."Specifications"->>'{def.JsonProperty}')::int AS "Value"
                    FROM scoped_products s
                    WHERE s."Specifications" IS NOT NULL
                      AND jsonb_typeof(s."Specifications") = 'object'
                      AND (s."Specifications"->>'{def.JsonProperty}') ~ '^[0-9]+$'
                      AND (s."Specifications"->>'{def.JsonProperty}')::int > 0
                """);
            }
        }

        sqlBuilder.AppendLine("ORDER BY \"Key\", \"Value\";");

        var groupRows = await _context.Database
            .SqlQueryRaw<FilterOptionGroupRow>(sqlBuilder.ToString(), parameters.ToArray())
            .ToListAsync(cancellationToken);

        var groups = new List<ProductFilterGroupDto>();
        foreach (var def in groupDefs)
        {
            var rawValues = groupRows
                .Where(r => r.Key == def.Key)
                .Select(r => r.Value);

            if (def.EnumType != null)
            {
                rawValues = rawValues.Where(v => Enum.IsDefined(def.EnumType, v));
            }
            else
            {
                rawValues = rawValues.Where(v => v > 0);
            }

            var values = rawValues.Distinct().OrderBy(v => v).ToList();
            groups.Add(new ProductFilterGroupDto(def.Key, values));
        }

        return groups;
    }

    private static void AppendScopeConditions(
        StringBuilder sqlBuilder,
        List<object> parameters,
        ProductFilterScope scope)
    {
        if (scope.ComponentType.HasValue)
        {
            sqlBuilder.AppendLine(" AND p.\"ComponentType\" = @p_componentType");
            parameters.Add(new NpgsqlParameter("p_componentType", (int)scope.ComponentType.Value));
        }

        if (scope.CategoryIds != null)
        {
            if (scope.CategoryIds.Count > 0)
            {
                sqlBuilder.AppendLine(" AND p.\"CategoryId\" = ANY(@p_categoryIds)");
                var param = new NpgsqlParameter("p_categoryIds", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Uuid)
                {
                    Value = scope.CategoryIds.ToArray()
                };
                parameters.Add(param);
            }
            else
            {
                sqlBuilder.AppendLine(" AND 1=0");
            }
        }

        if (scope.IsActive.HasValue)
        {
            sqlBuilder.AppendLine(" AND p.\"IsActive\" = @p_isActive");
            parameters.Add(new NpgsqlParameter("p_isActive", scope.IsActive.Value));
        }

        if (scope.InStock.HasValue)
        {
            if (scope.InStock.Value)
            {
                sqlBuilder.AppendLine(" AND p.\"StockQuantity\" > 0");
            }
            else
            {
                sqlBuilder.AppendLine(" AND p.\"StockQuantity\" = 0");
            }
        }
    }

    private static IReadOnlyList<GroupDefinition> GetGroupDefinitions(ComponentType componentType) =>
        componentType switch
        {
            ComponentType.Cpu =>
            [
                new GroupDefinition("sockets", nameof(CpuSpecification.Socket), IsArray: false, typeof(CpuSocket)),
                new GroupDefinition("memoryTypes", nameof(CpuSpecification.SupportedMemoryType), IsArray: false, typeof(RamType)),
                new GroupDefinition("coreCounts", nameof(CpuSpecification.CoreCount), IsArray: false, null)
            ],
            ComponentType.Motherboard =>
            [
                new GroupDefinition("sockets", nameof(MotherboardSpecification.Socket), IsArray: false, typeof(CpuSocket)),
                new GroupDefinition("memoryTypes", nameof(MotherboardSpecification.MemoryType), IsArray: false, typeof(RamType)),
                new GroupDefinition("motherboardFormFactors", nameof(MotherboardSpecification.FormFactor), IsArray: false, typeof(MotherboardFormFactor))
            ],
            ComponentType.Gpu =>
            [
                new GroupDefinition("vramGbs", nameof(GpuSpecification.VramGb), IsArray: false, null),
                new GroupDefinition("gpuLengthsMm", nameof(GpuSpecification.LengthMm), IsArray: false, null)
            ],
            ComponentType.Ram =>
            [
                new GroupDefinition("memoryTypes", nameof(RamSpecification.MemoryType), IsArray: false, typeof(RamType)),
                new GroupDefinition("ramFormFactors", nameof(RamSpecification.FormFactor), IsArray: false, typeof(RamFormFactor)),
                new GroupDefinition("ramCapacitiesGb", nameof(RamSpecification.CapacityGb), IsArray: false, null),
                new GroupDefinition("ramSpeedsMhz", nameof(RamSpecification.SpeedMhz), IsArray: false, null)
            ],
            ComponentType.Storage =>
            [
                new GroupDefinition("storageTypes", nameof(StorageSpecification.StorageType), IsArray: false, typeof(StorageType)),
                new GroupDefinition("storageInterfaces", nameof(StorageSpecification.Interface), IsArray: false, typeof(StorageInterface)),
                new GroupDefinition("storageCapacitiesGb", nameof(StorageSpecification.CapacityGb), IsArray: false, null)
            ],
            ComponentType.Psu =>
            [
                new GroupDefinition("psuEfficiencies", nameof(PsuSpecification.Efficiency), IsArray: false, typeof(PsuEfficiency)),
                new GroupDefinition("psuModularities", nameof(PsuSpecification.Modularity), IsArray: false, typeof(PsuModularity)),
                new GroupDefinition("psuFormFactors", nameof(PsuSpecification.FormFactor), IsArray: false, typeof(PsuFormFactor)),
                new GroupDefinition("psuWattages", nameof(PsuSpecification.Wattage), IsArray: false, null)
            ],
            ComponentType.Case =>
            [
                new GroupDefinition("motherboardFormFactors", nameof(CaseSpecification.SupportedFormFactors), IsArray: true, typeof(MotherboardFormFactor))
            ],
            ComponentType.Cooler =>
            [
                new GroupDefinition("sockets", nameof(CoolerSpecification.SupportedSockets), IsArray: true, typeof(CpuSocket)),
                new GroupDefinition("coolerTypes", nameof(CoolerSpecification.CoolerType), IsArray: false, typeof(CoolerType))
            ],
            _ => []
        };

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

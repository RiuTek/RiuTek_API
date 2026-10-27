using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RiuTek.Application.Common.Interfaces;
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

        // 1. Socket: Cpu, Motherboard (Socket), Cooler (SupportedSockets contains value)
        if (options.Socket.HasValue)
        {
            if (componentType is ComponentType.Cpu or ComponentType.Motherboard)
            {
                var json = CreateScalarJson(nameof(CpuSpecification.Socket), (int)options.Socket.Value);
                query = query.Where(p => EF.Functions.JsonContains(p.Specifications, json));
            }
            else if (componentType is ComponentType.Cooler)
            {
                var json = CreateArrayElementJson(nameof(CoolerSpecification.SupportedSockets), (int)options.Socket.Value);
                query = query.Where(p => EF.Functions.JsonContains(p.Specifications, json));
            }
        }

        // 2. MemoryType: Cpu (SupportedMemoryType), Motherboard / Ram (MemoryType)
        if (options.MemoryType.HasValue)
        {
            if (componentType is ComponentType.Cpu)
            {
                var json = CreateScalarJson(nameof(CpuSpecification.SupportedMemoryType), (int)options.MemoryType.Value);
                query = query.Where(p => EF.Functions.JsonContains(p.Specifications, json));
            }
            else if (componentType is ComponentType.Motherboard or ComponentType.Ram)
            {
                var json = CreateScalarJson(nameof(MotherboardSpecification.MemoryType), (int)options.MemoryType.Value);
                query = query.Where(p => EF.Functions.JsonContains(p.Specifications, json));
            }
        }

        // 3. MotherboardFormFactor: Motherboard (FormFactor), Case (SupportedFormFactors contains value)
        if (options.MotherboardFormFactor.HasValue)
        {
            if (componentType is ComponentType.Motherboard)
            {
                var json = CreateScalarJson(nameof(MotherboardSpecification.FormFactor), (int)options.MotherboardFormFactor.Value);
                query = query.Where(p => EF.Functions.JsonContains(p.Specifications, json));
            }
            else if (componentType is ComponentType.Case)
            {
                var json = CreateArrayElementJson(nameof(CaseSpecification.SupportedFormFactors), (int)options.MotherboardFormFactor.Value);
                query = query.Where(p => EF.Functions.JsonContains(p.Specifications, json));
            }
        }

        // 4. RamFormFactor: Ram (FormFactor)
        if (options.RamFormFactor.HasValue && componentType is ComponentType.Ram)
        {
            var json = CreateScalarJson(nameof(RamSpecification.FormFactor), (int)options.RamFormFactor.Value);
            query = query.Where(p => EF.Functions.JsonContains(p.Specifications, json));
        }

        // 5. StorageType: Storage (StorageType)
        if (options.StorageType.HasValue && componentType is ComponentType.Storage)
        {
            var json = CreateScalarJson(nameof(StorageSpecification.StorageType), (int)options.StorageType.Value);
            query = query.Where(p => EF.Functions.JsonContains(p.Specifications, json));
        }

        // 6. StorageInterface: Storage (Interface)
        if (options.StorageInterface.HasValue && componentType is ComponentType.Storage)
        {
            var json = CreateScalarJson(nameof(StorageSpecification.Interface), (int)options.StorageInterface.Value);
            query = query.Where(p => EF.Functions.JsonContains(p.Specifications, json));
        }

        // 7. PsuEfficiency: Psu (Efficiency)
        if (options.PsuEfficiency.HasValue && componentType is ComponentType.Psu)
        {
            var json = CreateScalarJson(nameof(PsuSpecification.Efficiency), (int)options.PsuEfficiency.Value);
            query = query.Where(p => EF.Functions.JsonContains(p.Specifications, json));
        }

        // 8. PsuModularity: Psu (Modularity)
        if (options.PsuModularity.HasValue && componentType is ComponentType.Psu)
        {
            var json = CreateScalarJson(nameof(PsuSpecification.Modularity), (int)options.PsuModularity.Value);
            query = query.Where(p => EF.Functions.JsonContains(p.Specifications, json));
        }

        // 9. PsuFormFactor: Psu (FormFactor)
        if (options.PsuFormFactor.HasValue && componentType is ComponentType.Psu)
        {
            var json = CreateScalarJson(nameof(PsuSpecification.FormFactor), (int)options.PsuFormFactor.Value);
            query = query.Where(p => EF.Functions.JsonContains(p.Specifications, json));
        }

        // 10. CoolerType: Cooler (CoolerType)
        if (options.CoolerType.HasValue && componentType is ComponentType.Cooler)
        {
            var json = CreateScalarJson(nameof(CoolerSpecification.CoolerType), (int)options.CoolerType.Value);
            query = query.Where(p => EF.Functions.JsonContains(p.Specifications, json));
        }

        // 11. HasIntegratedGpu: Cpu (HasIntegratedGpu)
        if (options.HasIntegratedGpu.HasValue && componentType is ComponentType.Cpu)
        {
            var json = CreateScalarJson(nameof(CpuSpecification.HasIntegratedGpu), options.HasIntegratedGpu.Value);
            query = query.Where(p => EF.Functions.JsonContains(p.Specifications, json));
        }

        // 12. HasWifi: Motherboard (HasWifi)
        if (options.HasWifi.HasValue && componentType is ComponentType.Motherboard)
        {
            var json = CreateScalarJson(nameof(MotherboardSpecification.HasWifi), options.HasWifi.Value);
            query = query.Where(p => EF.Functions.JsonContains(p.Specifications, json));
        }

        // 13. HasRgb: Ram / Cooler (HasRgb)
        if (options.HasRgb.HasValue && componentType is ComponentType.Ram or ComponentType.Cooler)
        {
            var json = CreateScalarJson(nameof(RamSpecification.HasRgb), options.HasRgb.Value);
            query = query.Where(p => EF.Functions.JsonContains(p.Specifications, json));
        }

        // 14. Has12VHPWR: Psu (Has12VHPWR)
        if (options.Has12VHPWR.HasValue && componentType is ComponentType.Psu)
        {
            var json = CreateScalarJson(nameof(PsuSpecification.Has12VHPWR), options.Has12VHPWR.Value);
            query = query.Where(p => EF.Functions.JsonContains(p.Specifications, json));
        }

        // 15. Requires12VHPWR: Gpu (Requires12VHPWR)
        if (options.Requires12VHPWR.HasValue && componentType is ComponentType.Gpu)
        {
            var json = CreateScalarJson(nameof(GpuSpecification.Requires12VHPWR), options.Requires12VHPWR.Value);
            query = query.Where(p => EF.Functions.JsonContains(p.Specifications, json));
        }

        return query;
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
}

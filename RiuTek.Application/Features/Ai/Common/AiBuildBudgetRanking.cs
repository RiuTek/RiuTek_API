using RiuTek.Core.Entities;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Features.Ai.Common;

public static class AiBuildBudgetRanking
{
    // Relative shopping priorities. These scores do not represent benchmark performance.
    public static decimal Weight(ComponentType type, string useCase) => (useCase, type) switch
    {
        ("gaming", ComponentType.Gpu) => 40,
        ("gaming", ComponentType.Cpu) => 22,
        ("creation", ComponentType.Cpu) => 30,
        ("creation", ComponentType.Gpu) => 25,
        ("creation", ComponentType.Ram) => 15,
        ("office", ComponentType.Cpu) => 30,
        ("office", ComponentType.Gpu) => 0,
        ("office", ComponentType.Ram) => 16,
        (_, ComponentType.Motherboard) => 10,
        (_, ComponentType.Ram) => 8,
        (_, ComponentType.Storage) => 10,
        (_, ComponentType.Psu) => 7,
        (_, ComponentType.Case) => 4,
        (_, ComponentType.Cooler) => 4,
        _ => 1
    };

    public static decimal Score(Product? part, string useCase, decimal target, decimal budget)
    {
        if (part == null) return useCase == "office" ? 1 : 0;
        var weight = Weight(part.ComponentType, useCase);
        var capacity = part.Specifications switch
        {
            CpuSpecification cpu => Math.Min(cpu.CoreCount / (useCase == "creation" ? 12m : 6m), 1),
            RamSpecification ram => Math.Min(ram.CapacityGb / (useCase == "creation" ? 32m : 16m), 1),
            StorageSpecification storage => Math.Min(storage.CapacityGb / (useCase == "office" ? 512m : 1000m), 1),
            GpuSpecification gpu => Math.Min(gpu.VramGb / 16m, 1),
            _ => 0m
        };
        var value = part.Specifications switch
        {
            CpuSpecification cpu when useCase == "office" => capacity + (cpu.HasIntegratedGpu ? 0.5m : 0),
            RamSpecification => capacity,
            StorageSpecification storage => capacity + (storage.StorageType is StorageType.NVMe_M2 or StorageType.SATA_SSD ? 0.1m : 0),
            CpuSpecification or GpuSpecification when target > 0 =>
                0.7m * Math.Min(part.Price / target, 1) + 0.3m * capacity,
            _ => 0m
        };
        // Once capacity/target is satisfied, prefer the less expensive equivalent.
        return weight * value - part.Price / Math.Max(budget, 1) * 0.1m;
    }
}

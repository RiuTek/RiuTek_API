using System.Text.Json.Serialization;
using RiuTek.Core.Enums;

namespace RiuTek.Core.Entities.Specifications;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(CpuSpecification), typeDiscriminator: "cpu")]
[JsonDerivedType(typeof(MotherboardSpecification), typeDiscriminator: "motherboard")]
[JsonDerivedType(typeof(GpuSpecification), typeDiscriminator: "gpu")]
[JsonDerivedType(typeof(RamSpecification), typeDiscriminator: "ram")]
[JsonDerivedType(typeof(StorageSpecification), typeDiscriminator: "storage")]
[JsonDerivedType(typeof(PsuSpecification), typeDiscriminator: "psu")]
[JsonDerivedType(typeof(CaseSpecification), typeDiscriminator: "case")]
[JsonDerivedType(typeof(CoolerSpecification), typeDiscriminator: "cooler")]
[JsonDerivedType(typeof(AccessorySpecification), typeDiscriminator: "accessory")]
public abstract record ComponentSpecification
{
    public abstract ComponentType ComponentType { get; }
}

public record CpuSpecification : ComponentSpecification
{
    public override ComponentType ComponentType => ComponentType.Cpu;

    public CpuSocket Socket { get; init; }
    public int CoreCount { get; init; }
    public int ThreadCount { get; init; }
    public double BaseClockGhz { get; init; }
    public double BoostClockGhz { get; init; }
    public int TdpWattage { get; init; }
    public bool HasIntegratedGpu { get; init; }
    public RamType SupportedMemoryType { get; init; }
    public int MaxMemorySpeedMhz { get; init; }
}

public record MotherboardSpecification : ComponentSpecification
{
    public override ComponentType ComponentType => ComponentType.Motherboard;

    public CpuSocket Socket { get; init; }
    public string Chipset { get; init; } = string.Empty;
    public MotherboardFormFactor FormFactor { get; init; }
    public RamType MemoryType { get; init; }
    public int MemorySlots { get; init; } = 4;
    public int MaxMemoryCapacityGb { get; init; } = 128;
    public int M2Slots { get; init; } = 2;
    public int SataPorts { get; init; } = 4;
    public int PcieSlots { get; init; } = 2;
    public bool HasWifi { get; init; }
}

public record GpuSpecification : ComponentSpecification
{
    public override ComponentType ComponentType => ComponentType.Gpu;

    public string Chipset { get; init; } = string.Empty;
    public int VramGb { get; init; }
    public string VramType { get; init; } = "GDDR6";
    public int LengthMm { get; init; }
    public double SlotWidth { get; init; } = 2.0;
    public int TdpWattage { get; init; }
    public int RecommendedPsuWattage { get; init; }
    public bool Requires12VHPWR { get; init; }
    public string PowerConnectors { get; init; } = string.Empty;
}

public record RamSpecification : ComponentSpecification
{
    public override ComponentType ComponentType => ComponentType.Ram;

    public RamType MemoryType { get; init; }
    public RamFormFactor FormFactor { get; init; } = RamFormFactor.DIMM;
    public int CapacityGb { get; init; } // Total capacity of the kit
    public int ModuleCount { get; init; } = 2; // e.g., 2 for 2x16GB
    public int SpeedMhz { get; init; }
    public int CasLatency { get; init; }
    public bool HasRgb { get; init; }
}

public record StorageSpecification : ComponentSpecification
{
    public override ComponentType ComponentType => ComponentType.Storage;

    public StorageType StorageType { get; init; }
    public StorageInterface Interface { get; init; }
    public int CapacityGb { get; init; }
    public int ReadSpeedMBs { get; init; }
    public int WriteSpeedMBs { get; init; }
    public string FormFactor { get; init; } = "M.2 2280";
}

public record PsuSpecification : ComponentSpecification
{
    public override ComponentType ComponentType => ComponentType.Psu;

    public int Wattage { get; init; }
    public PsuEfficiency Efficiency { get; init; } = PsuEfficiency.Plus80Gold;
    public PsuModularity Modularity { get; init; } = PsuModularity.FullModular;
    public PsuFormFactor FormFactor { get; init; } = PsuFormFactor.ATX;
    public bool Has12VHPWR { get; init; }
    public int LengthMm { get; init; } = 140;
}

public record CaseSpecification : ComponentSpecification
{
    public override ComponentType ComponentType => ComponentType.Case;

    public List<MotherboardFormFactor> SupportedFormFactors { get; init; } = [];
    public int MaxGpuLengthMm { get; init; }
    public int MaxCpuCoolerHeightMm { get; init; }
    public int MaxPsuLengthMm { get; init; } = 200;
    public List<int> SupportedRadiatorSizesMm { get; init; } = []; // e.g., [240, 280, 360]
    public int IncludedFans { get; init; }
}

public record CoolerSpecification : ComponentSpecification
{
    public override ComponentType ComponentType => ComponentType.Cooler;

    public CoolerType CoolerType { get; init; }
    public List<CpuSocket> SupportedSockets { get; init; } = [];
    public int HeightMm { get; init; } // for Air coolers
    public int RadiatorSizeMm { get; init; } // for AIO coolers (e.g. 240, 360)
    public int MaxTdpRating { get; init; }
    public bool HasRgb { get; init; }
}

public record AccessorySpecification : ComponentSpecification
{
    public override ComponentType ComponentType => ComponentType.Accessory;

    public string Details { get; init; } = string.Empty;
}

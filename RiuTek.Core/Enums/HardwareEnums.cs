namespace RiuTek.Core.Enums;

public enum ComponentType
{
    Cpu = 1,
    Motherboard = 2,
    Gpu = 3,
    Ram = 4,
    Storage = 5,
    Psu = 6,
    Case = 7,
    Cooler = 8,
    Accessory = 9
}

public enum CpuSocket
{
    LGA1700 = 1,
    LGA1851 = 2,
    LGA1200 = 3,
    AM4 = 4,
    AM5 = 5,
    TR4 = 6,
    sTRX4 = 7,
    Other = 99
}

public enum RamType
{
    DDR4 = 1,
    DDR5 = 2,
    DDR3 = 3,
    LPDDR5 = 4
}

public enum RamFormFactor
{
    DIMM = 1,
    SODIMM = 2
}

public enum MotherboardFormFactor
{
    ATX = 1,
    MicroATX = 2,
    MiniITX = 3,
    EATX = 4
}

public enum StorageType
{
    NVMe_M2 = 1,
    SATA_SSD = 2,
    HDD_3_5 = 3,
    HDD_2_5 = 4
}

public enum StorageInterface
{
    PCIe_3_0 = 1,
    PCIe_4_0 = 2,
    PCIe_5_0 = 3,
    SATA_3 = 4
}

public enum PsuEfficiency
{
    Plus80 = 1,
    Plus80Bronze = 2,
    Plus80Silver = 3,
    Plus80Gold = 4,
    Plus80Platinum = 5,
    Plus80Titanium = 6
}

public enum PsuModularity
{
    NonModular = 1,
    SemiModular = 2,
    FullModular = 3
}

public enum PsuFormFactor
{
    ATX = 1,
    SFX = 2,
    SFX_L = 3,
    TFX = 4
}

public enum CoolerType
{
    Air = 1,
    AIO_120 = 2,
    AIO_240 = 3,
    AIO_280 = 4,
    AIO_360 = 5,
    AIO_420 = 6,
    CustomLoop = 7
}

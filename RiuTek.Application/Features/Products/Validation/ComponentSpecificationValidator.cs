using FluentValidation;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Features.Products.Validation;

public class ComponentSpecificationValidator : AbstractValidator<ComponentSpecification>
{
    public ComponentSpecificationValidator()
    {
        RuleFor(x => x).NotNull().WithMessage("Thông số kỹ thuật không được để trống.");

        When(x => x is CpuSpecification, () =>
        {
            RuleFor(x => (CpuSpecification)x)
                .Custom((cpu, context) =>
                {
                    if (!Enum.IsDefined(typeof(CpuSocket), cpu.Socket))
                        context.AddFailure(nameof(cpu.Socket), "Socket CPU không hợp lệ.");

                    if (cpu.CoreCount <= 0)
                        context.AddFailure(nameof(cpu.CoreCount), "Số nhân CPU phải lớn hơn 0.");

                    if (cpu.ThreadCount <= 0)
                        context.AddFailure(nameof(cpu.ThreadCount), "Số luồng CPU phải lớn hơn 0.");

                    if (cpu.BaseClockGhz <= 0)
                        context.AddFailure(nameof(cpu.BaseClockGhz), "Xung nhịp cơ bản phải lớn hơn 0.");

                    if (cpu.BoostClockGhz <= 0)
                        context.AddFailure(nameof(cpu.BoostClockGhz), "Xung nhịp tối đa phải lớn hơn 0.");

                    if (cpu.BoostClockGhz < cpu.BaseClockGhz)
                        context.AddFailure(nameof(cpu.BoostClockGhz), "Xung nhịp tối đa không được nhỏ hơn xung nhịp cơ bản.");

                    if (cpu.TdpWattage <= 0)
                        context.AddFailure(nameof(cpu.TdpWattage), "TDP của CPU phải lớn hơn 0.");

                    if (!Enum.IsDefined(typeof(RamType), cpu.SupportedMemoryType))
                        context.AddFailure(nameof(cpu.SupportedMemoryType), "Loại RAM hỗ trợ không hợp lệ.");

                    if (cpu.MaxMemorySpeedMhz <= 0)
                        context.AddFailure(nameof(cpu.MaxMemorySpeedMhz), "Tốc độ RAM hỗ trợ tối đa phải lớn hơn 0.");
                });
        });

        When(x => x is MotherboardSpecification, () =>
        {
            RuleFor(x => (MotherboardSpecification)x)
                .Custom((mb, context) =>
                {
                    if (!Enum.IsDefined(typeof(CpuSocket), mb.Socket))
                        context.AddFailure(nameof(mb.Socket), "Socket bo mạch chủ không hợp lệ.");

                    if (string.IsNullOrWhiteSpace(mb.Chipset))
                        context.AddFailure(nameof(mb.Chipset), "Chipset bo mạch chủ không được để trống.");

                    if (!Enum.IsDefined(typeof(MotherboardFormFactor), mb.FormFactor))
                        context.AddFailure(nameof(mb.FormFactor), "Kích thước chuẩn (Form Factor) không hợp lệ.");

                    if (!Enum.IsDefined(typeof(RamType), mb.MemoryType))
                        context.AddFailure(nameof(mb.MemoryType), "Loại RAM của bo mạch chủ không hợp lệ.");

                    if (mb.MemorySlots <= 0)
                        context.AddFailure(nameof(mb.MemorySlots), "Số khe cắm RAM phải lớn hơn 0.");

                    if (mb.MaxMemoryCapacityGb <= 0)
                        context.AddFailure(nameof(mb.MaxMemoryCapacityGb), "Dung lượng RAM hỗ trợ tối đa phải lớn hơn 0.");

                    if (mb.M2Slots < 0)
                        context.AddFailure(nameof(mb.M2Slots), "Số khe M.2 không được âm.");

                    if (mb.SataPorts < 0)
                        context.AddFailure(nameof(mb.SataPorts), "Số cổng SATA không được âm.");

                    if (mb.PcieSlots < 0)
                        context.AddFailure(nameof(mb.PcieSlots), "Số khe PCIe không được âm.");
                });
        });

        When(x => x is GpuSpecification, () =>
        {
            RuleFor(x => (GpuSpecification)x)
                .Custom((gpu, context) =>
                {
                    if (string.IsNullOrWhiteSpace(gpu.Chipset))
                        context.AddFailure(nameof(gpu.Chipset), "Chipset GPU không được để trống.");

                    if (gpu.VramGb <= 0)
                        context.AddFailure(nameof(gpu.VramGb), "Dung lượng VRAM phải lớn hơn 0.");

                    if (gpu.LengthMm <= 0)
                        context.AddFailure(nameof(gpu.LengthMm), "Chiều dài GPU phải lớn hơn 0.");

                    if (gpu.SlotWidth <= 0)
                        context.AddFailure(nameof(gpu.SlotWidth), "Độ dày khe cắm (Slot Width) phải lớn hơn 0.");

                    if (gpu.TdpWattage <= 0)
                        context.AddFailure(nameof(gpu.TdpWattage), "TDP của GPU phải lớn hơn 0.");

                    if (gpu.RecommendedPsuWattage <= 0)
                        context.AddFailure(nameof(gpu.RecommendedPsuWattage), "Công suất nguồn khuyến nghị phải lớn hơn 0.");
                });
        });

        When(x => x is RamSpecification, () =>
        {
            RuleFor(x => (RamSpecification)x)
                .Custom((ram, context) =>
                {
                    if (!Enum.IsDefined(typeof(RamType), ram.MemoryType))
                        context.AddFailure(nameof(ram.MemoryType), "Loại RAM không hợp lệ.");

                    if (!Enum.IsDefined(typeof(RamFormFactor), ram.FormFactor))
                        context.AddFailure(nameof(ram.FormFactor), "Kích thước chuẩn RAM không hợp lệ.");

                    if (ram.CapacityGb <= 0)
                        context.AddFailure(nameof(ram.CapacityGb), "Dung lượng RAM phải lớn hơn 0.");

                    if (ram.ModuleCount <= 0)
                        context.AddFailure(nameof(ram.ModuleCount), "Số lượng thanh RAM phải lớn hơn 0.");

                    if (ram.SpeedMhz <= 0)
                        context.AddFailure(nameof(ram.SpeedMhz), "Tốc độ RAM (Bus) phải lớn hơn 0.");

                    if (ram.CasLatency <= 0)
                        context.AddFailure(nameof(ram.CasLatency), "Độ trễ CAS phải lớn hơn 0.");
                });
        });

        When(x => x is StorageSpecification, () =>
        {
            RuleFor(x => (StorageSpecification)x)
                .Custom((storage, context) =>
                {
                    if (!Enum.IsDefined(typeof(StorageType), storage.StorageType))
                        context.AddFailure(nameof(storage.StorageType), "Loại ổ cứng không hợp lệ.");

                    if (!Enum.IsDefined(typeof(StorageInterface), storage.Interface))
                        context.AddFailure(nameof(storage.Interface), "Chuẩn giao tiếp ổ cứng không hợp lệ.");

                    if (storage.CapacityGb <= 0)
                        context.AddFailure(nameof(storage.CapacityGb), "Dung lượng ổ cứng phải lớn hơn 0.");

                    if (storage.ReadSpeedMBs < 0)
                        context.AddFailure(nameof(storage.ReadSpeedMBs), "Tốc độ đọc không được âm.");

                    if (storage.WriteSpeedMBs < 0)
                        context.AddFailure(nameof(storage.WriteSpeedMBs), "Tốc độ ghi không được âm.");

                    if (string.IsNullOrWhiteSpace(storage.FormFactor))
                        context.AddFailure(nameof(storage.FormFactor), "Kích thước chuẩn (Form Factor) không được để trống.");
                });
        });

        When(x => x is PsuSpecification, () =>
        {
            RuleFor(x => (PsuSpecification)x)
                .Custom((psu, context) =>
                {
                    if (psu.Wattage <= 0)
                        context.AddFailure(nameof(psu.Wattage), "Công suất nguồn phải lớn hơn 0.");

                    if (!Enum.IsDefined(typeof(PsuEfficiency), psu.Efficiency))
                        context.AddFailure(nameof(psu.Efficiency), "Chuẩn hiệu suất nguồn không hợp lệ.");

                    if (!Enum.IsDefined(typeof(PsuModularity), psu.Modularity))
                        context.AddFailure(nameof(psu.Modularity), "Kiểu dây cắm (Modularity) không hợp lệ.");

                    if (!Enum.IsDefined(typeof(PsuFormFactor), psu.FormFactor))
                        context.AddFailure(nameof(psu.FormFactor), "Kích thước chuẩn nguồn không hợp lệ.");

                    if (psu.LengthMm <= 0)
                        context.AddFailure(nameof(psu.LengthMm), "Chiều dài nguồn phải lớn hơn 0.");
                });
        });

        When(x => x is CaseSpecification, () =>
        {
            RuleFor(x => (CaseSpecification)x)
                .Custom((cs, context) =>
                {
                    if (cs.SupportedFormFactors == null || cs.SupportedFormFactors.Count == 0)
                    {
                        context.AddFailure(nameof(cs.SupportedFormFactors), "Danh sách form factor bo mạch chủ hỗ trợ không được để trống.");
                    }
                    else
                    {
                        var distinctCount = cs.SupportedFormFactors.Distinct().Count();
                        if (distinctCount != cs.SupportedFormFactors.Count)
                            context.AddFailure(nameof(cs.SupportedFormFactors), "Danh sách form factor chứa phần tử trùng lặp.");

                        foreach (var ff in cs.SupportedFormFactors)
                        {
                            if (!Enum.IsDefined(typeof(MotherboardFormFactor), ff))
                                context.AddFailure(nameof(cs.SupportedFormFactors), $"Form factor '{ff}' không hợp lệ.");
                        }
                    }

                    if (cs.MaxGpuLengthMm <= 0)
                        context.AddFailure(nameof(cs.MaxGpuLengthMm), "Chiều dài GPU hỗ trợ tối đa phải lớn hơn 0.");

                    if (cs.MaxCpuCoolerHeightMm <= 0)
                        context.AddFailure(nameof(cs.MaxCpuCoolerHeightMm), "Chiều cao tản nhiệt CPU hỗ trợ tối đa phải lớn hơn 0.");

                    if (cs.MaxPsuLengthMm <= 0)
                        context.AddFailure(nameof(cs.MaxPsuLengthMm), "Chiều dài nguồn hỗ trợ tối đa phải lớn hơn 0.");

                    if (cs.SupportedRadiatorSizesMm != null)
                    {
                        foreach (var rad in cs.SupportedRadiatorSizesMm)
                        {
                            if (rad < 0)
                                context.AddFailure(nameof(cs.SupportedRadiatorSizesMm), "Kích thước tản nhiệt nước hỗ trợ không được âm.");
                        }
                    }

                    if (cs.IncludedFans < 0)
                        context.AddFailure(nameof(cs.IncludedFans), "Số lượng quạt đi kèm không được âm.");
                });
        });

        When(x => x is CoolerSpecification, () =>
        {
            RuleFor(x => (CoolerSpecification)x)
                .Custom((cooler, context) =>
                {
                    if (!Enum.IsDefined(typeof(CoolerType), cooler.CoolerType))
                        context.AddFailure(nameof(cooler.CoolerType), "Loại tản nhiệt không hợp lệ.");

                    if (cooler.SupportedSockets == null || cooler.SupportedSockets.Count == 0)
                    {
                        context.AddFailure(nameof(cooler.SupportedSockets), "Danh sách socket CPU hỗ trợ không được để trống.");
                    }
                    else
                    {
                        var distinctCount = cooler.SupportedSockets.Distinct().Count();
                        if (distinctCount != cooler.SupportedSockets.Count)
                            context.AddFailure(nameof(cooler.SupportedSockets), "Danh sách socket chứa phần tử trùng lặp.");

                        foreach (var socket in cooler.SupportedSockets)
                        {
                            if (!Enum.IsDefined(typeof(CpuSocket), socket))
                                context.AddFailure(nameof(cooler.SupportedSockets), $"Socket '{socket}' không hợp lệ.");
                        }
                    }

                    if (cooler.MaxTdpRating <= 0)
                        context.AddFailure(nameof(cooler.MaxTdpRating), "Công suất tản nhiệt tối đa (TDP) phải lớn hơn 0.");

                    if (cooler.CoolerType == CoolerType.Air)
                    {
                        if (cooler.HeightMm <= 0)
                            context.AddFailure(nameof(cooler.HeightMm), "Chiều cao tản nhiệt khí phải lớn hơn 0.");
                    }
                    else if (cooler.CoolerType is CoolerType.AIO_120 or CoolerType.AIO_240 or CoolerType.AIO_280 or CoolerType.AIO_360 or CoolerType.AIO_420)
                    {
                        if (cooler.RadiatorSizeMm <= 0)
                            context.AddFailure(nameof(cooler.RadiatorSizeMm), "Kích thước két nước (Radiator) của tản nhiệt AIO phải lớn hơn 0.");
                    }
                    else if (cooler.CoolerType == CoolerType.CustomLoop)
                    {
                        if (cooler.HeightMm < 0)
                            context.AddFailure(nameof(cooler.HeightMm), "Chiều cao không được âm.");
                        if (cooler.RadiatorSizeMm < 0)
                            context.AddFailure(nameof(cooler.RadiatorSizeMm), "Kích thước két nước không được âm.");
                    }
                });
        });

        When(x => x is AccessorySpecification, () =>
        {
            RuleFor(x => (AccessorySpecification)x)
                .Custom((acc, context) =>
                {
                    if (string.IsNullOrWhiteSpace(acc.Details))
                        context.AddFailure(nameof(acc.Details), "Chi tiết phụ kiện không được để trống.");
                    else if (acc.Details.Length > 2000)
                        context.AddFailure(nameof(acc.Details), "Chi tiết phụ kiện không được vượt quá 2000 ký tự.");
                });
        });

        RuleFor(x => x)
            .Must(x => x is CpuSpecification or MotherboardSpecification or GpuSpecification
                     or RamSpecification or StorageSpecification or PsuSpecification
                     or CaseSpecification or CoolerSpecification or AccessorySpecification)
            .WithMessage("Loại thông số kỹ thuật không được hỗ trợ.");
    }
}

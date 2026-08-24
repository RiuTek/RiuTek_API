using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Core.Entities;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Services;

public class HardwareCompatibilityService : IHardwareCompatibilityService
{
    public CompatibilityCheckResultDto ValidateComponents(IReadOnlyList<Product> components)
    {
        var specs = components
            .Where(c => c.Specifications != null)
            .Select(c => c.Specifications)
            .ToList();

        return ValidateSpecifications(specs);
    }
//test
    public CompatibilityCheckResultDto ValidateSpecifications(IReadOnlyList<ComponentSpecification> specifications)
    {
        var issues = new List<CompatibilityIssueDto>();

        // Extract individual component specifications (if present)
        var cpu = specifications.OfType<CpuSpecification>().FirstOrDefault();
        var mobo = specifications.OfType<MotherboardSpecification>().FirstOrDefault();
        var ramList = specifications.OfType<RamSpecification>().ToList();
        var gpu = specifications.OfType<GpuSpecification>().FirstOrDefault();
        var psu = specifications.OfType<PsuSpecification>().FirstOrDefault();
        var pcCase = specifications.OfType<CaseSpecification>().FirstOrDefault();
        var cooler = specifications.OfType<CoolerSpecification>().FirstOrDefault();
        var storageList = specifications.OfType<StorageSpecification>().ToList();

        // -------------------------------------------------------------
        // RULE 1: CPU & Motherboard Socket Compatibility
        // -------------------------------------------------------------
        if (cpu != null && mobo != null)
        {
            if (cpu.Socket != mobo.Socket)
            {
                issues.Add(new CompatibilityIssueDto(
                    "COMP-01",
                    "Error",
                    $"CPU ({cpu.Socket})",
                    $"Motherboard ({mobo.Socket})",
                    $"CPU sử dụng socket {cpu.Socket} không thể gắn vào Mainboard sử dụng socket {mobo.Socket}."
                ));
            }
        }

        // -------------------------------------------------------------
        // RULE 2, 3, 4: RAM & Motherboard Compatibility
        // -------------------------------------------------------------
        if (ramList.Count != 0 && mobo != null)
        {
            var totalRamModules = ramList.Sum(r => r.ModuleCount);
            var totalRamCapacity = ramList.Sum(r => r.CapacityGb);

            foreach (var ram in ramList)
            {
                if (ram.MemoryType != mobo.MemoryType)
                {
                    issues.Add(new CompatibilityIssueDto(
                        "COMP-02",
                        "Error",
                        $"RAM ({ram.MemoryType})",
                        $"Motherboard ({mobo.MemoryType})",
                        $"RAM chuẩn {ram.MemoryType} không tương thích với Mainboard chỉ hỗ trợ chuẩn {mobo.MemoryType}."
                    ));
                }
            }

            if (totalRamModules > mobo.MemorySlots)
            {
                issues.Add(new CompatibilityIssueDto(
                    "COMP-03",
                    "Error",
                    $"RAM Modules ({totalRamModules} thanh)",
                    $"Motherboard ({mobo.MemorySlots} khe cắm)",
                    $"Tổng số thanh RAM ({totalRamModules}) vượt quá số khe cắm RAM của Mainboard ({mobo.MemorySlots} khe)."
                ));
            }

            if (totalRamCapacity > mobo.MaxMemoryCapacityGb)
            {
                issues.Add(new CompatibilityIssueDto(
                    "COMP-04",
                    "Error",
                    $"RAM Capacity ({totalRamCapacity} GB)",
                    $"Motherboard Max ({mobo.MaxMemoryCapacityGb} GB)",
                    $"Tổng dung lượng RAM ({totalRamCapacity} GB) vượt quá dung lượng tối đa Mainboard hỗ trợ ({mobo.MaxMemoryCapacityGb} GB)."
                ));
            }
        }

        // -------------------------------------------------------------
        // RULE 5: Motherboard & Case Form Factor Compatibility
        // -------------------------------------------------------------
        if (mobo != null && pcCase != null && pcCase.SupportedFormFactors.Count > 0)
        {
            if (!pcCase.SupportedFormFactors.Contains(mobo.FormFactor))
            {
                var supportedList = string.Join(", ", pcCase.SupportedFormFactors);
                issues.Add(new CompatibilityIssueDto(
                    "COMP-05",
                    "Error",
                    $"Motherboard ({mobo.FormFactor})",
                    $"Case (Hỗ trợ: {supportedList})",
                    $"Kích thước Mainboard ({mobo.FormFactor}) không thể lắp vừa Vỏ Case (chỉ hỗ trợ: {supportedList})."
                ));
            }
        }

        // -------------------------------------------------------------
        // RULE 6: GPU Physical Length vs Case Clearance
        // -------------------------------------------------------------
        if (gpu != null && pcCase != null && pcCase.MaxGpuLengthMm > 0 && gpu.LengthMm > 0)
        {
            if (gpu.LengthMm > pcCase.MaxGpuLengthMm)
            {
                issues.Add(new CompatibilityIssueDto(
                    "COMP-06",
                    "Error",
                    $"GPU Length ({gpu.LengthMm} mm)",
                    $"Case Max GPU ({pcCase.MaxGpuLengthMm} mm)",
                    $"Chiều dài Card đồ họa ({gpu.LengthMm} mm) vượt quá chiều dài tối đa Vỏ Case cho phép ({pcCase.MaxGpuLengthMm} mm)."
                ));
            }
        }

        // -------------------------------------------------------------
        // RULE 7, 8, 9: Cooler Compatibility (CPU Socket, Case Clearance, TDP)
        // -------------------------------------------------------------
        if (cooler != null)
        {
            if (cpu != null && cooler.SupportedSockets.Count > 0)
            {
                if (!cooler.SupportedSockets.Contains(cpu.Socket))
                {
                    issues.Add(new CompatibilityIssueDto(
                        "COMP-07",
                        "Error",
                        $"Cooler (Socket hỗ trợ: {string.Join(", ", cooler.SupportedSockets)})",
                        $"CPU ({cpu.Socket})",
                        $"Tản nhiệt không có gông hỗ trợ Socket {cpu.Socket} của CPU đã chọn."
                    ));
                }
            }

            if (pcCase != null)
            {
                // Air Cooler Height
                if (cooler.CoolerType == CoolerType.Air && cooler.HeightMm > 0 && pcCase.MaxCpuCoolerHeightMm > 0)
                {
                    if (cooler.HeightMm > pcCase.MaxCpuCoolerHeightMm)
                    {
                        issues.Add(new CompatibilityIssueDto(
                            "COMP-08",
                            "Error",
                            $"Cooler Height ({cooler.HeightMm} mm)",
                            $"Case Max Height ({pcCase.MaxCpuCoolerHeightMm} mm)",
                            $"Chiều cao tản nhiệt khí ({cooler.HeightMm} mm) cao hơn khoảng trống cho phép của Vỏ Case ({pcCase.MaxCpuCoolerHeightMm} mm)."
                        ));
                    }
                }
                // AIO Liquid Cooler Radiator
                else if (cooler.CoolerType != CoolerType.Air && cooler.RadiatorSizeMm > 0 && pcCase.SupportedRadiatorSizesMm.Count > 0)
                {
                    if (!pcCase.SupportedRadiatorSizesMm.Contains(cooler.RadiatorSizeMm))
                    {
                        issues.Add(new CompatibilityIssueDto(
                            "COMP-08",
                            "Error",
                            $"AIO Radiator ({cooler.RadiatorSizeMm} mm)",
                            $"Case Radiator (Hỗ trợ: {string.Join(", ", pcCase.SupportedRadiatorSizesMm)} mm)",
                            $"Kích thước két làm mát nước ({cooler.RadiatorSizeMm} mm) không vừa các vị trí gắn tản của Vỏ Case."
                        ));
                    }
                }
            }

            if (cpu != null && cooler.MaxTdpRating > 0 && cpu.TdpWattage > 0)
            {
                if (cooler.MaxTdpRating < cpu.TdpWattage)
                {
                    issues.Add(new CompatibilityIssueDto(
                        "COMP-09",
                        "Warning",
                        $"Cooler Max TDP ({cooler.MaxTdpRating} W)",
                        $"CPU TDP ({cpu.TdpWattage} W)",
                        $"Hiệu năng tản nhiệt ({cooler.MaxTdpRating} W) thấp hơn công suất tỏa nhiệt của CPU ({cpu.TdpWattage} W). CPU có thể bị quá nhiệt khi chạy tải nặng."
                    ));
                }
            }
        }

        // -------------------------------------------------------------
        // Power Consumption & PSU Calculation
        // -------------------------------------------------------------
        var estimatedWattage = CalculateEstimatedWattage(cpu, gpu, mobo, ramList, storageList);
        var recommendedPsuWattage = CalculateRecommendedPsu(estimatedWattage);

        if (psu != null && psu.Wattage > 0)
        {
            if (psu.Wattage < estimatedWattage)
            {
                issues.Add(new CompatibilityIssueDto(
                    "COMP-10",
                    "Error",
                    $"PSU ({psu.Wattage} W)",
                    $"Dàn máy cần ({estimatedWattage} W)",
                    $"Công suất Nguồn ({psu.Wattage} W) thấp hơn tổng điện năng tiêu thụ ước tính của dàn máy ({estimatedWattage} W). Hệ thống có thể sập nguồn khi tải nặng."
                ));
            }
            else if (psu.Wattage < recommendedPsuWattage)
            {
                issues.Add(new CompatibilityIssueDto(
                    "COMP-10",
                    "Warning",
                    $"PSU ({psu.Wattage} W)",
                    $"Đề xuất ({recommendedPsuWattage} W)",
                    $"Công suất Nguồn ({psu.Wattage} W) có thể đáp ứng cơ bản nhưng chưa đạt hệ số an toàn 25% khuyến nghị ({recommendedPsuWattage} W)."
                ));
            }

            if (gpu != null && gpu.Requires12VHPWR && !psu.Has12VHPWR)
            {
                issues.Add(new CompatibilityIssueDto(
                    "COMP-11",
                    "Warning",
                    $"GPU ({gpu.Chipset} cần 12VHPWR)",
                    $"PSU ({psu.Efficiency})",
                    "Card đồ họa sử dụng cổng nguồn chuẩn mới 12VHPWR. Nguồn bạn chọn chưa có cáp này trực tiếp, bạn sẽ cần dùng cáp chuyển đổi đi kèm card."
                ));
            }
        }

        // -------------------------------------------------------------
        // System Completeness Check (Informational Only)
        // -------------------------------------------------------------
        var missingComponents = new List<string>();
        if (cpu == null) missingComponents.Add("CPU");
        if (mobo == null) missingComponents.Add("Mainboard");
        if (ramList.Count == 0) missingComponents.Add("RAM");
        if (storageList.Count == 0) missingComponents.Add("Ổ cứng (SSD/HDD)");
        if (psu == null) missingComponents.Add("Nguồn (PSU)");
        if (pcCase == null) missingComponents.Add("Vỏ Case");

        // IsCompatible is true if there are NO ERRORS (Warnings do not block compatibility)
        var isCompatible = issues.All(i => i.Severity != "Error");
        var isCompleteSystem = missingComponents.Count == 0;

        return new CompatibilityCheckResultDto(
            IsCompatible: isCompatible,
            IsCompleteSystem: isCompleteSystem,
            MissingComponents: missingComponents,
            EstimatedWattage: estimatedWattage,
            RecommendedPsuWattage: recommendedPsuWattage,
            Issues: issues
        );
    }

    private static int CalculateEstimatedWattage(
        CpuSpecification? cpu,
        GpuSpecification? gpu,
        MotherboardSpecification? mobo,
        List<RamSpecification> ramList,
        List<StorageSpecification> storageList)
    {
        var total = 0;

        // CPU Wattage
        if (cpu != null && cpu.TdpWattage > 0)
        {
            total += cpu.TdpWattage;
        }

        // GPU Wattage
        if (gpu != null && gpu.TdpWattage > 0)
        {
            total += gpu.TdpWattage;
        }

        // Base Motherboard + Chipset + System Overhead (if any components are present)
        if (mobo != null || cpu != null || gpu != null)
        {
            total += 50; // Standard motherboard baseline
        }

        // RAM modules (~5W per module)
        if (ramList.Count != 0)
        {
            var totalModules = ramList.Sum(r => r.ModuleCount);
            total += totalModules * 5;
        }

        // Storage drives (~5W for M.2 / SATA SSD, ~10W for HDD)
        foreach (var storage in storageList)
        {
            total += storage.StorageType is StorageType.HDD_3_5 or StorageType.HDD_2_5 ? 10 : 5;
        }

        // Fans, RGB, Pump allowance
        if (total > 0)
        {
            total += 25;
        }

        return total;
    }

    private static int CalculateRecommendedPsu(int estimatedWattage)
    {
        if (estimatedWattage <= 0) return 0;

        // Multiply by 1.25 (25% safety headroom) and round up to the nearest 50W
        var withHeadroom = estimatedWattage * 1.25;
        var rounded = (int)(Math.Ceiling(withHeadroom / 50.0) * 50);

        return Math.Max(450, rounded); // Minimum standard ATX PSU baseline 450W
    }
}

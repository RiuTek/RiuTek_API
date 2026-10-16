using System.IO.Compression;
using System.Text;
using ClosedXML.Excel;
using FluentAssertions;
using RiuTek.Application.DTOs;
using RiuTek.Core.Enums;
using RiuTek.Infrastructure.Services;

namespace RiuTek.Application.Test.Features.Products;

public class ProductImportWorkbookServiceTests
{
    private readonly ClosedXmlProductImportWorkbookService _service = new();

    private static readonly CategoryLookupDto[] SampleCategories =
    [
        new("cpu", "Vi xử lý", ComponentType.Cpu),
        new("mainboard", "Bo mạch chủ", ComponentType.Motherboard),
        new("vga", "Card màn hình", ComponentType.Gpu),
        new("ram", "Bộ nhớ RAM", ComponentType.Ram),
        new("ssd", "Ổ cứng lưu trữ", ComponentType.Storage),
        new("psu", "Nguồn máy tính", ComponentType.Psu),
        new("case", "Vỏ máy tính", ComponentType.Case),
        new("cooler", "Tản nhiệt", ComponentType.Cooler),
        new("accessory", "Phụ kiện", ComponentType.Accessory)
    ];

    [Fact]
    public async Task GenerateTemplateAsync_ShouldContainAllRequiredSheets_MetadataAndExactHeaders()
    {
        // Act
        var template = await _service.GenerateTemplateAsync(SampleCategories);

        // Assert
        template.Should().NotBeNull();
        template.ContentType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        template.FileName.Should().Be("riutek-product-import-v1.0.xlsx");
        template.Content.Should().NotBeEmpty();

        using var ms = new MemoryStream(template.Content);
        using var workbook = new XLWorkbook(ms);

        string[] expectedSheets =
        [
            "Instructions", "Products", "CPU", "Motherboard", "GPU", "RAM",
            "Storage", "PSU", "Case", "Cooler", "Accessory", "_Lookups", "_Meta"
        ];

        workbook.Worksheets.Select(w => w.Name).Should().ContainInOrder(expectedSheets);

        // Verify _Meta
        var metaWs = workbook.Worksheet("_Meta");
        metaWs.Cell("A2").GetString().Should().Be("TemplateName");
        metaWs.Cell("B2").GetString().Should().Be("RiuTekProductImport");
        metaWs.Cell("A3").GetString().Should().Be("TemplateVersion");
        metaWs.Cell("B3").GetString().Should().Be("1.0");
        metaWs.Cell("A4").GetString().Should().Be("ListSeparator");
        metaWs.Cell("B4").GetString().Should().Be("|");

        // Verify Products headers
        var prodHeaders = workbook.Worksheet("Products").Row(1).CellsUsed().Select(c => c.GetString()).ToList();
        prodHeaders.Should().Equal(
            "SKU", "Name", "CategorySlug", "ComponentType", "Brand",
            "Price", "OriginalPrice", "StockQuantity", "PrimaryImageFile", "ImageUrl");

        // Verify CPU headers
        var cpuHeaders = workbook.Worksheet("CPU").Row(1).CellsUsed().Select(c => c.GetString()).ToList();
        cpuHeaders.Should().Equal(
            "SKU", "Socket", "CoreCount", "ThreadCount", "BaseClockGhz",
            "BoostClockGhz", "TdpWattage", "HasIntegratedGpu", "SupportedMemoryType", "MaxMemorySpeedMhz");

        // Verify Lookups contains categories
        var lookupsWs = workbook.Worksheet("_Lookups");
        lookupsWs.Cell("L2").GetString().Should().Be("cpu");
        lookupsWs.Cell("M2").GetString().Should().Be("Vi xử lý");
    }

    [Fact]
    public async Task ParseWorkbookAsync_ValidCpuWorkbook_ShouldParseSuccessfully()
    {
        // Arrange
        var template = await _service.GenerateTemplateAsync(SampleCategories);
        using var ms = new MemoryStream(template.Content);
        using var workbook = new XLWorkbook(ms);

        // Populate Products
        var pWs = workbook.Worksheet("Products");
        pWs.Cell(2, 1).Value = "CPU-AMD-7800X3D";
        pWs.Cell(2, 2).Value = "AMD Ryzen 7 7800X3D";
        pWs.Cell(2, 3).Value = "cpu";
        pWs.Cell(2, 4).Value = "Cpu";
        pWs.Cell(2, 5).Value = "AMD";
        pWs.Cell(2, 6).Value = 10500000;
        pWs.Cell(2, 7).Value = 11500000;
        pWs.Cell(2, 8).Value = 20;
        pWs.Cell(2, 9).Value = "7800x3d.png";
        pWs.Cell(2, 10).Value = "https://media.riutek.com/products/cpu.png";

        // Populate CPU spec
        var cpuWs = workbook.Worksheet("CPU");
        cpuWs.Cell(2, 1).Value = "CPU-AMD-7800X3D";
        cpuWs.Cell(2, 2).Value = "AM5";
        cpuWs.Cell(2, 3).Value = 8;
        cpuWs.Cell(2, 4).Value = 16;
        cpuWs.Cell(2, 5).Value = 4.2;
        cpuWs.Cell(2, 6).Value = 5.0;
        cpuWs.Cell(2, 7).Value = 120;
        cpuWs.Cell(2, 8).Value = true;
        cpuWs.Cell(2, 9).Value = "DDR5";
        cpuWs.Cell(2, 10).Value = 5200;

        using var saveMs = new MemoryStream();
        workbook.SaveAs(saveMs);
        saveMs.Position = 0;

        // Act
        var result = await _service.ParseWorkbookAsync(saveMs);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.TotalProductRows.Should().Be(1);
        result.Value.ParseErrors.Should().BeEmpty();
        result.Value.Rows.Should().HaveCount(1);

        var row = result.Value.Rows[0];
        row.Sku.Should().Be("CPU-AMD-7800X3D");
        row.ComponentType.Should().Be(ComponentType.Cpu);
        row.Specification.Should().NotBeNull();
        row.Specification!.ComponentType.Should().Be(ComponentType.Cpu);
    }

    [Fact]
    public async Task ParseWorkbookAsync_ValidMixedWorkbook_All9Subtypes_ShouldParseSuccessfully()
    {
        // Arrange
        var template = await _service.GenerateTemplateAsync(SampleCategories);
        using var ms = new MemoryStream(template.Content);
        using var workbook = new XLWorkbook(ms);

        var pWs = workbook.Worksheet("Products");

        // 1. CPU
        pWs.Cell(2, 1).Value = "SKU-CPU"; pWs.Cell(2, 2).Value = "CPU Intel i5"; pWs.Cell(2, 3).Value = "cpu"; pWs.Cell(2, 4).Value = "Cpu"; pWs.Cell(2, 5).Value = "Intel"; pWs.Cell(2, 6).Value = 5000000; pWs.Cell(2, 8).Value = 10; pWs.Cell(2, 10).Value = "https://media.riutek.com/cpu.png";
        var cpuWs = workbook.Worksheet("CPU");
        cpuWs.Cell(2, 1).Value = "SKU-CPU"; cpuWs.Cell(2, 2).Value = "LGA1700"; cpuWs.Cell(2, 3).Value = 10; cpuWs.Cell(2, 4).Value = 16; cpuWs.Cell(2, 5).Value = 2.5; cpuWs.Cell(2, 6).Value = 4.6; cpuWs.Cell(2, 7).Value = 65; cpuWs.Cell(2, 8).Value = true; cpuWs.Cell(2, 9).Value = "DDR5"; cpuWs.Cell(2, 10).Value = 4800;

        // 2. Motherboard
        pWs.Cell(3, 1).Value = "SKU-MB"; pWs.Cell(3, 2).Value = "MB ASUS B760"; pWs.Cell(3, 3).Value = "mainboard"; pWs.Cell(3, 4).Value = "Motherboard"; pWs.Cell(3, 5).Value = "ASUS"; pWs.Cell(3, 6).Value = 3500000; pWs.Cell(3, 8).Value = 10; pWs.Cell(3, 10).Value = "https://media.riutek.com/mb.png";
        var mbWs = workbook.Worksheet("Motherboard");
        mbWs.Cell(2, 1).Value = "SKU-MB"; mbWs.Cell(2, 2).Value = "LGA1700"; mbWs.Cell(2, 3).Value = "B760"; mbWs.Cell(2, 4).Value = "ATX"; mbWs.Cell(2, 5).Value = "DDR5"; mbWs.Cell(2, 6).Value = 4; mbWs.Cell(2, 7).Value = 192; mbWs.Cell(2, 8).Value = 3; mbWs.Cell(2, 9).Value = 4; mbWs.Cell(2, 10).Value = 2; mbWs.Cell(2, 11).Value = true;

        // 3. GPU
        pWs.Cell(4, 1).Value = "SKU-GPU"; pWs.Cell(4, 2).Value = "GPU RTX 4070"; pWs.Cell(4, 3).Value = "vga"; pWs.Cell(4, 4).Value = "Gpu"; pWs.Cell(4, 5).Value = "NVIDIA"; pWs.Cell(4, 6).Value = 16000000; pWs.Cell(4, 8).Value = 5; pWs.Cell(4, 10).Value = "https://media.riutek.com/gpu.png";
        var gpuWs = workbook.Worksheet("GPU");
        gpuWs.Cell(2, 1).Value = "SKU-GPU"; gpuWs.Cell(2, 2).Value = "AD104"; gpuWs.Cell(2, 3).Value = 12; gpuWs.Cell(2, 4).Value = "GDDR6X"; gpuWs.Cell(2, 5).Value = 242; gpuWs.Cell(2, 6).Value = 2.0; gpuWs.Cell(2, 7).Value = 200; gpuWs.Cell(2, 8).Value = 650; gpuWs.Cell(2, 9).Value = true; gpuWs.Cell(2, 10).Value = "1x 16-pin";

        // 4. RAM
        pWs.Cell(5, 1).Value = "SKU-RAM"; pWs.Cell(5, 2).Value = "RAM Corsair 32GB"; pWs.Cell(5, 3).Value = "ram"; pWs.Cell(5, 4).Value = "Ram"; pWs.Cell(5, 5).Value = "Corsair"; pWs.Cell(5, 6).Value = 2800000; pWs.Cell(5, 8).Value = 15; pWs.Cell(5, 10).Value = "https://media.riutek.com/ram.png";
        var ramWs = workbook.Worksheet("RAM");
        ramWs.Cell(2, 1).Value = "SKU-RAM"; ramWs.Cell(2, 2).Value = "DDR5"; ramWs.Cell(2, 3).Value = "DIMM"; ramWs.Cell(2, 4).Value = 32; ramWs.Cell(2, 5).Value = 2; ramWs.Cell(2, 6).Value = 6000; ramWs.Cell(2, 7).Value = 30; ramWs.Cell(2, 8).Value = true;

        // 5. Storage
        pWs.Cell(6, 1).Value = "SKU-SSD"; pWs.Cell(6, 2).Value = "SSD Samsung 1TB"; pWs.Cell(6, 3).Value = "ssd"; pWs.Cell(6, 4).Value = "Storage"; pWs.Cell(6, 5).Value = "Samsung"; pWs.Cell(6, 6).Value = 2200000; pWs.Cell(6, 8).Value = 20; pWs.Cell(6, 10).Value = "https://media.riutek.com/ssd.png";
        var ssdWs = workbook.Worksheet("Storage");
        ssdWs.Cell(2, 1).Value = "SKU-SSD"; ssdWs.Cell(2, 2).Value = "NVMe_M2"; ssdWs.Cell(2, 3).Value = "PCIe_4_0"; ssdWs.Cell(2, 4).Value = 1000; ssdWs.Cell(2, 5).Value = 7450; ssdWs.Cell(2, 6).Value = 6900; ssdWs.Cell(2, 7).Value = "M.2 2280";

        // 6. PSU
        pWs.Cell(7, 1).Value = "SKU-PSU"; pWs.Cell(7, 2).Value = "PSU Seasonic 750W"; pWs.Cell(7, 3).Value = "psu"; pWs.Cell(7, 4).Value = "Psu"; pWs.Cell(7, 5).Value = "Seasonic"; pWs.Cell(7, 6).Value = 2900000; pWs.Cell(7, 8).Value = 10; pWs.Cell(7, 10).Value = "https://media.riutek.com/psu.png";
        var psuWs = workbook.Worksheet("PSU");
        psuWs.Cell(2, 1).Value = "SKU-PSU"; psuWs.Cell(2, 2).Value = 750; psuWs.Cell(2, 3).Value = "Plus80Gold"; psuWs.Cell(2, 4).Value = "FullModular"; psuWs.Cell(2, 5).Value = "ATX"; psuWs.Cell(2, 6).Value = true; psuWs.Cell(2, 7).Value = 140;

        // 7. Case
        pWs.Cell(8, 1).Value = "SKU-CASE"; pWs.Cell(8, 2).Value = "Case NZXT H5"; pWs.Cell(8, 3).Value = "case"; pWs.Cell(8, 4).Value = "Case"; pWs.Cell(8, 5).Value = "NZXT"; pWs.Cell(8, 6).Value = 2100000; pWs.Cell(8, 8).Value = 8; pWs.Cell(8, 10).Value = "https://media.riutek.com/case.png";
        var caseWs = workbook.Worksheet("Case");
        caseWs.Cell(2, 1).Value = "SKU-CASE"; caseWs.Cell(2, 2).Value = "ATX|MicroATX|MiniITX"; caseWs.Cell(2, 3).Value = 365; caseWs.Cell(2, 4).Value = 165; caseWs.Cell(2, 5).Value = 200; caseWs.Cell(2, 6).Value = "240|280"; caseWs.Cell(2, 7).Value = 2;

        // 8. Cooler
        pWs.Cell(9, 1).Value = "SKU-COOLER"; pWs.Cell(9, 2).Value = "Cooler Deepcool AK620"; pWs.Cell(9, 3).Value = "cooler"; pWs.Cell(9, 4).Value = "Cooler"; pWs.Cell(9, 5).Value = "Deepcool"; pWs.Cell(9, 6).Value = 1400000; pWs.Cell(9, 8).Value = 12; pWs.Cell(9, 10).Value = "https://media.riutek.com/cooler.png";
        var coolerWs = workbook.Worksheet("Cooler");
        coolerWs.Cell(2, 1).Value = "SKU-COOLER"; coolerWs.Cell(2, 2).Value = "Air"; coolerWs.Cell(2, 3).Value = "AM4|AM5|LGA1700"; coolerWs.Cell(2, 4).Value = 160; coolerWs.Cell(2, 5).Value = 0; coolerWs.Cell(2, 6).Value = 260; coolerWs.Cell(2, 7).Value = false;

        // 9. Accessory
        pWs.Cell(10, 1).Value = "SKU-ACC"; pWs.Cell(10, 2).Value = "Thermal Paste MX-4"; pWs.Cell(10, 3).Value = "accessory"; pWs.Cell(10, 4).Value = "Accessory"; pWs.Cell(10, 5).Value = "Arctic"; pWs.Cell(10, 6).Value = 150000; pWs.Cell(10, 8).Value = 50; pWs.Cell(10, 10).Value = "https://media.riutek.com/mx4.png";
        var accWs = workbook.Worksheet("Accessory");
        accWs.Cell(2, 1).Value = "SKU-ACC"; accWs.Cell(2, 2).Value = "Keo tản nhiệt 4g";

        using var saveMs = new MemoryStream();
        workbook.SaveAs(saveMs);
        saveMs.Position = 0;

        // Act
        var result = await _service.ParseWorkbookAsync(saveMs);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.TotalProductRows.Should().Be(9);
        result.Value.ParseErrors.Should().BeEmpty();
        result.Value.Rows.Should().HaveCount(9);
    }

    [Fact]
    public async Task ParseWorkbookAsync_InvalidEnumsAndCellTypes_ShouldReportAccurateCoordinates()
    {
        // Arrange
        var template = await _service.GenerateTemplateAsync(SampleCategories);
        using var ms = new MemoryStream(template.Content);
        using var workbook = new XLWorkbook(ms);

        var pWs = workbook.Worksheet("Products");
        pWs.Cell(2, 1).Value = "SKU-ERR";
        pWs.Cell(2, 2).Value = "Test Product";
        pWs.Cell(2, 3).Value = "cpu";
        pWs.Cell(2, 4).Value = "InvalidComponentType"; // Invalid enum
        pWs.Cell(2, 5).Value = "Brand";
        pWs.Cell(2, 6).Value = "ABC_NOT_A_NUMBER"; // Invalid numeric
        pWs.Cell(2, 8).Value = 10;
        pWs.Cell(2, 10).Value = "https://media.riutek.com/img.png";

        using var saveMs = new MemoryStream();
        workbook.SaveAs(saveMs);
        saveMs.Position = 0;

        // Act
        var result = await _service.ParseWorkbookAsync(saveMs);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var errors = result.Value.ParseErrors;
        errors.Should().Contain(e => e.Sheet == "Products" && e.Row == 2 && e.Column == "ComponentType" && e.Code == "ProductImport.InvalidEnum");
        errors.Should().Contain(e => e.Sheet == "Products" && e.Row == 2 && e.Column == "Price" && e.Code == "ProductImport.InvalidCellType");
    }

    [Fact]
    public async Task ParseWorkbookAsync_FormulasInCells_ShouldReportFormulaNotAllowed()
    {
        // Arrange
        var template = await _service.GenerateTemplateAsync(SampleCategories);
        using var ms = new MemoryStream(template.Content);
        using var workbook = new XLWorkbook(ms);

        var pWs = workbook.Worksheet("Products");
        pWs.Cell(2, 1).Value = "SKU-FORMULA";
        pWs.Cell(2, 2).Value = "Formula Product";
        pWs.Cell(2, 3).Value = "cpu";
        pWs.Cell(2, 4).Value = "Cpu";
        pWs.Cell(2, 5).Value = "Intel";
        pWs.Cell(2, 6).FormulaA1 = "=1000*5"; // Formula!
        pWs.Cell(2, 8).Value = 10;
        pWs.Cell(2, 10).Value = "https://media.riutek.com/img.png";

        using var saveMs = new MemoryStream();
        workbook.SaveAs(saveMs);
        saveMs.Position = 0;

        // Act
        var result = await _service.ParseWorkbookAsync(saveMs);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.ParseErrors.Should().Contain(e =>
            e.Sheet == "Products" && e.Row == 2 && e.Code == "ProductImport.FormulaNotAllowed");
    }

    [Fact]
    public async Task ParseWorkbookAsync_OrphanAndWrongSheetSpecs_ShouldBeDetected()
    {
        // Arrange
        var template = await _service.GenerateTemplateAsync(SampleCategories);
        using var ms = new MemoryStream(template.Content);
        using var workbook = new XLWorkbook(ms);

        var pWs = workbook.Worksheet("Products");
        pWs.Cell(2, 1).Value = "SKU-CPU-1";
        pWs.Cell(2, 2).Value = "Intel i3";
        pWs.Cell(2, 3).Value = "cpu";
        pWs.Cell(2, 4).Value = "Cpu";
        pWs.Cell(2, 5).Value = "Intel";
        pWs.Cell(2, 6).Value = 2500000;
        pWs.Cell(2, 8).Value = 10;
        pWs.Cell(2, 10).Value = "https://media.riutek.com/img.png";

        // Spec in wrong sheet (GPU instead of CPU)
        var gpuWs = workbook.Worksheet("GPU");
        gpuWs.Cell(2, 1).Value = "SKU-CPU-1";
        gpuWs.Cell(2, 2).Value = "Chip";
        gpuWs.Cell(2, 3).Value = 8;
        gpuWs.Cell(2, 4).Value = "GDDR6";
        gpuWs.Cell(2, 5).Value = 200;
        gpuWs.Cell(2, 6).Value = 2.0;
        gpuWs.Cell(2, 7).Value = 150;
        gpuWs.Cell(2, 8).Value = 500;
        gpuWs.Cell(2, 9).Value = false;
        gpuWs.Cell(2, 10).Value = "8-pin";

        // Orphan spec (SKU doesn't exist in Products)
        var cpuWs = workbook.Worksheet("CPU");
        cpuWs.Cell(2, 1).Value = "SKU-GHOST-ORPHAN";
        cpuWs.Cell(2, 2).Value = "AM4";
        cpuWs.Cell(2, 3).Value = 6;
        cpuWs.Cell(2, 4).Value = 12;
        cpuWs.Cell(2, 5).Value = 3.6;
        cpuWs.Cell(2, 6).Value = 4.2;
        cpuWs.Cell(2, 7).Value = 65;
        cpuWs.Cell(2, 8).Value = false;
        cpuWs.Cell(2, 9).Value = "DDR4";
        cpuWs.Cell(2, 10).Value = 3200;

        using var saveMs = new MemoryStream();
        workbook.SaveAs(saveMs);
        saveMs.Position = 0;

        // Act
        var result = await _service.ParseWorkbookAsync(saveMs);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var errors = result.Value.ParseErrors;
        errors.Should().Contain(e => e.Code == "ProductImport.SpecificationTypeMismatch" && e.Sku == "SKU-CPU-1");
        errors.Should().Contain(e => e.Code == "ProductImport.OrphanSpecification" && e.Sku == "SKU-GHOST-ORPHAN");
        errors.Should().Contain(e => e.Code == "ProductImport.MissingSpecification" && e.Sku == "SKU-CPU-1");
    }

    [Fact]
    public async Task ParseWorkbookAsync_CorruptedOrMacroZip_ShouldFailGracefully()
    {
        // Corrupt stream
        using var corruptStream = new MemoryStream(Encoding.UTF8.GetBytes("THIS IS NOT A VALID ZIP"));
        var corruptResult = await _service.ParseWorkbookAsync(corruptStream);
        corruptResult.IsSuccess.Should().BeFalse();
        corruptResult.Error.Code.Should().Be("ProductImport.CorruptedFile");

        // Macro archive simulation
        using var macroMs = new MemoryStream();
        using (var archive = new ZipArchive(macroMs, ZipArchiveMode.Create, true))
        {
            var entry = archive.CreateEntry("xl/vbaProject.bin");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("DUMMY VBA");
        }
        macroMs.Position = 0;

        var macroResult = await _service.ParseWorkbookAsync(macroMs);
        macroResult.IsSuccess.Should().BeFalse();
        macroResult.Error.Code.Should().Be("ProductImport.MacroNotAllowed");
    }

    [Fact]
    public async Task ParseWorkbookAsync_UnsupportedVersion_ShouldReject()
    {
        var template = await _service.GenerateTemplateAsync(SampleCategories);
        using var ms = new MemoryStream(template.Content);
        using var workbook = new XLWorkbook(ms);

        var metaWs = workbook.Worksheet("_Meta");
        metaWs.Cell("B3").Value = "99.9"; // Invalid version!

        using var saveMs = new MemoryStream();
        workbook.SaveAs(saveMs);
        saveMs.Position = 0;

        var result = await _service.ParseWorkbookAsync(saveMs);
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("ProductImport.UnsupportedTemplateVersion");
    }

    [Fact]
    public async Task ParseWorkbookAsync_TooManyRows_ShouldReject()
    {
        var template = await _service.GenerateTemplateAsync(SampleCategories);
        using var ms = new MemoryStream(template.Content);
        using var workbook = new XLWorkbook(ms);

        var pWs = workbook.Worksheet("Products");
        for (int r = 2; r <= 1002; r++) // 1001 rows
        {
            pWs.Cell(r, 1).Value = $"SKU-{r}";
            pWs.Cell(r, 2).Value = $"Product {r}";
            pWs.Cell(r, 3).Value = "cpu";
            pWs.Cell(r, 4).Value = "Cpu";
            pWs.Cell(r, 5).Value = "Brand";
            pWs.Cell(r, 6).Value = 1000;
            pWs.Cell(r, 8).Value = 1;
            pWs.Cell(r, 10).Value = "https://media.riutek.com/img.png";
        }

        using var saveMs = new MemoryStream();
        workbook.SaveAs(saveMs);
        saveMs.Position = 0;

        var result = await _service.ParseWorkbookAsync(saveMs);
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("ProductImport.TooManyRows");
    }

    [Fact]
    public async Task ParseWorkbookAsync_EmptyWorkbook_ShouldReturnNoProductsValidationError()
    {
        var template = await _service.GenerateTemplateAsync(SampleCategories);
        using var stream = new MemoryStream(template.Content);

        var result = await _service.ParseWorkbookAsync(stream);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalProductRows.Should().Be(0);
        result.Value.ParseErrors.Should().ContainSingle(e => e.Code == "ProductImport.NoProducts");
    }

    [Fact]
    public async Task ParseWorkbookAsync_NumericEnumValue_ShouldBeRejected()
    {
        var template = await _service.GenerateTemplateAsync(SampleCategories);
        using var ms = new MemoryStream(template.Content);
        using var workbook = new XLWorkbook(ms);

        var products = workbook.Worksheet("Products");
        products.Cell(2, 1).Value = "SKU-NUMERIC-ENUM";
        products.Cell(2, 2).Value = "Numeric Enum Product";
        products.Cell(2, 3).Value = "cpu";
        products.Cell(2, 4).Value = 1;
        products.Cell(2, 5).Value = "Brand";
        products.Cell(2, 6).Value = 1000;
        products.Cell(2, 8).Value = 1;
        products.Cell(2, 10).Value = "https://media.riutek.test/product.webp";

        using var saveMs = new MemoryStream();
        workbook.SaveAs(saveMs);
        saveMs.Position = 0;

        var result = await _service.ParseWorkbookAsync(saveMs);

        result.IsSuccess.Should().BeTrue();
        result.Value.ParseErrors.Should().Contain(e =>
            e.Code == "ProductImport.InvalidEnum" &&
            e.Sheet == "Products" &&
            e.Row == 2 &&
            e.Column == "ComponentType");
    }

    [Fact]
    public async Task ParseWorkbookAsync_CaseWithoutRadiatorSupport_ShouldRemainValid()
    {
        var template = await _service.GenerateTemplateAsync(SampleCategories);
        using var ms = new MemoryStream(template.Content);
        using var workbook = new XLWorkbook(ms);

        var products = workbook.Worksheet("Products");
        products.Cell(2, 1).Value = "CASE-NO-RADIATOR";
        products.Cell(2, 2).Value = "Compact Case";
        products.Cell(2, 3).Value = "case";
        products.Cell(2, 4).Value = "Case";
        products.Cell(2, 5).Value = "RiuTek";
        products.Cell(2, 6).Value = 1000000;
        products.Cell(2, 8).Value = 2;
        products.Cell(2, 10).Value = "https://media.riutek.test/case.webp";

        var cases = workbook.Worksheet("Case");
        cases.Cell(2, 1).Value = "CASE-NO-RADIATOR";
        cases.Cell(2, 2).Value = "MiniITX";
        cases.Cell(2, 3).Value = 250;
        cases.Cell(2, 4).Value = 120;
        cases.Cell(2, 5).Value = 160;
        cases.Cell(2, 7).Value = 1;

        using var saveMs = new MemoryStream();
        workbook.SaveAs(saveMs);
        saveMs.Position = 0;

        var result = await _service.ParseWorkbookAsync(saveMs);

        result.IsSuccess.Should().BeTrue();
        result.Value.ParseErrors.Should().BeEmpty();
        result.Value.Rows.Should().ContainSingle();
        var specification = result.Value.Rows[0].Specification.Should().BeOfType<RiuTek.Core.Entities.Specifications.CaseSpecification>().Subject;
        specification.SupportedRadiatorSizesMm.Should().BeEmpty();
    }
}

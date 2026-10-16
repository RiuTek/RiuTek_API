using System.Globalization;
using System.IO.Compression;
using ClosedXML.Excel;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Products.Common;
using RiuTek.Core.Common;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;

namespace RiuTek.Infrastructure.Services;

public class ClosedXmlProductImportWorkbookService : IProductImportWorkbookService
{
    private const string CurrentTemplateVersion = "1.0";
    private const string TemplateIdentifier = "RiuTekProductImport";
    private const char DefaultListSeparator = '|';

    private const int MaxZipEntries = 100;
    private const long MaxZipUncompressedBytes = 25 * 1024 * 1024; // 25 MiB
    private const int MaxProductRows = 1000;

    private static readonly string[] RequiredSheets =
    [
        "Instructions",
        "Products",
        "CPU",
        "Motherboard",
        "GPU",
        "RAM",
        "Storage",
        "PSU",
        "Case",
        "Cooler",
        "Accessory",
        "_Lookups",
        "_Meta"
    ];

    private static readonly Dictionary<string, string[]> SheetExpectedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        {
            "Products",
            ["SKU", "Name", "CategorySlug", "ComponentType", "Brand", "Price", "OriginalPrice", "StockQuantity", "PrimaryImageFile", "ImageUrl"]
        },
        {
            "CPU",
            ["SKU", "Socket", "CoreCount", "ThreadCount", "BaseClockGhz", "BoostClockGhz", "TdpWattage", "HasIntegratedGpu", "SupportedMemoryType", "MaxMemorySpeedMhz"]
        },
        {
            "Motherboard",
            ["SKU", "Socket", "Chipset", "FormFactor", "MemoryType", "MemorySlots", "MaxMemoryCapacityGb", "M2Slots", "SataPorts", "PcieSlots", "HasWifi"]
        },
        {
            "GPU",
            ["SKU", "Chipset", "VramGb", "VramType", "LengthMm", "SlotWidth", "TdpWattage", "RecommendedPsuWattage", "Requires12VHPWR", "PowerConnectors"]
        },
        {
            "RAM",
            ["SKU", "MemoryType", "FormFactor", "CapacityGb", "ModuleCount", "SpeedMhz", "CasLatency", "HasRgb"]
        },
        {
            "Storage",
            ["SKU", "StorageType", "Interface", "CapacityGb", "ReadSpeedMBs", "WriteSpeedMBs", "FormFactor"]
        },
        {
            "PSU",
            ["SKU", "Wattage", "Efficiency", "Modularity", "FormFactor", "Has12VHPWR", "LengthMm"]
        },
        {
            "Case",
            ["SKU", "SupportedFormFactors", "MaxGpuLengthMm", "MaxCpuCoolerHeightMm", "MaxPsuLengthMm", "SupportedRadiatorSizesMm", "IncludedFans"]
        },
        {
            "Cooler",
            ["SKU", "CoolerType", "SupportedSockets", "HeightMm", "RadiatorSizeMm", "MaxTdpRating", "HasRgb"]
        },
        {
            "Accessory",
            ["SKU", "Details"]
        }
    };

    public Task<ProductImportTemplateDto> GenerateTemplateAsync(
        IReadOnlyList<CategoryLookupDto> categories,
        CancellationToken cancellationToken = default)
    {
        using var workbook = new XLWorkbook();

        // 1. Instructions Sheet
        var wsInstructions = workbook.Worksheets.Add("Instructions");
        SetupInstructionsSheet(wsInstructions);

        // 2. Data Sheets
        foreach (var kvp in SheetExpectedHeaders)
        {
            var ws = workbook.Worksheets.Add(kvp.Key);
            SetupDataSheet(ws, kvp.Key, kvp.Value, categories);
        }

        // 3. _Lookups Sheet (Hidden)
        var wsLookups = workbook.Worksheets.Add("_Lookups");
        SetupLookupsSheet(wsLookups, categories);
        wsLookups.Visibility = XLWorksheetVisibility.Hidden;

        // 4. _Meta Sheet (VeryHidden)
        var wsMeta = workbook.Worksheets.Add("_Meta");
        SetupMetaSheet(wsMeta);
        wsMeta.Visibility = XLWorksheetVisibility.VeryHidden;

        // Reorder sheets to match exact logic order
        for (int i = 0; i < RequiredSheets.Length; i++)
        {
            if (workbook.TryGetWorksheet(RequiredSheets[i], out var sheet))
            {
                sheet.Position = i + 1;
            }
        }

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        var bytes = ms.ToArray();

        var templateDto = new ProductImportTemplateDto(
            bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"riutek-product-import-v{CurrentTemplateVersion}.xlsx");

        return Task.FromResult(templateDto);
    }

    public Task<Result<ProductImportParsedWorkbook>> ParseWorkbookAsync(
        Stream fileStream,
        CancellationToken cancellationToken = default)
    {
        using var memoryStream = new MemoryStream();
        fileStream.CopyTo(memoryStream);
        memoryStream.Position = 0;

        // 1. ZIP Container safety inspection
        var zipSafetyResult = InspectZipSafety(memoryStream);
        if (!zipSafetyResult.IsSuccess)
        {
            return Task.FromResult(Result.Failure<ProductImportParsedWorkbook>(zipSafetyResult.Error));
        }

        // 2. Load ClosedXML Workbook
        memoryStream.Position = 0;
        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(memoryStream);
        }
        catch (Exception)
        {
            return Task.FromResult(Result.Failure<ProductImportParsedWorkbook>(Error.Validation(
                "ProductImport.CorruptedFile",
                "Không thể mở file Excel. File bị hỏng hoặc sai cấu trúc OpenXML.")));
        }

        using (workbook)
        {
            // 3. Validate _Meta sheet
            if (!workbook.TryGetWorksheet("_Meta", out var wsMeta))
            {
                return Task.FromResult(Result.Failure<ProductImportParsedWorkbook>(Error.Validation(
                    "ProductImport.UnsupportedTemplateVersion",
                    "Không tìm thấy sheet '_Meta' trong workbook.")));
            }

            var metaEntries = ParseMetaEntries(wsMeta);
            if (!metaEntries.TryGetValue("TemplateName", out var templateName) ||
                !string.Equals(templateName, TemplateIdentifier, StringComparison.OrdinalIgnoreCase) ||
                !metaEntries.TryGetValue("TemplateVersion", out var templateVersion) ||
                !string.Equals(templateVersion, CurrentTemplateVersion, StringComparison.OrdinalIgnoreCase) ||
                !metaEntries.TryGetValue("ListSeparator", out var listSeparator) ||
                !string.Equals(listSeparator, DefaultListSeparator.ToString(), StringComparison.Ordinal))
            {
                return Task.FromResult(Result.Failure<ProductImportParsedWorkbook>(Error.Validation(
                    "ProductImport.UnsupportedTemplateVersion",
                    "Phiên bản template hoặc định danh template không được hỗ trợ.")));
            }

            var parseErrors = new List<ProductImportErrorDto>();

            // 4. Validate Required Sheets existence
            foreach (var reqSheet in RequiredSheets)
            {
                if (!workbook.TryGetWorksheet(reqSheet, out _))
                {
                    parseErrors.Add(new ProductImportErrorDto(
                        reqSheet,
                        1,
                        null,
                        null,
                        "ProductImport.MissingSheet",
                        $"Thiếu sheet bắt buộc '{reqSheet}'."));
                }
            }

            foreach (var worksheet in workbook.Worksheets)
            {
                if (!RequiredSheets.Contains(worksheet.Name, StringComparer.OrdinalIgnoreCase))
                {
                    parseErrors.Add(new ProductImportErrorDto(
                        worksheet.Name,
                        1,
                        null,
                        null,
                        "ProductImport.UnexpectedSheet",
                        $"Sheet '{worksheet.Name}' không thuộc schema template v{CurrentTemplateVersion}."));
                }
            }

            // 5. Validate Headers for data sheets
            var sheetsWithInvalidHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in SheetExpectedHeaders)
            {
                var sheetName = kvp.Key;
                var expectedHeaders = kvp.Value;

                if (!workbook.TryGetWorksheet(sheetName, out var ws))
                {
                    continue;
                }

                var headerValidationErrors = ValidateHeaders(ws, sheetName, expectedHeaders);
                if (headerValidationErrors.Count > 0)
                {
                    parseErrors.AddRange(headerValidationErrors);
                    sheetsWithInvalidHeaders.Add(sheetName);
                }
            }

            // If Products sheet is missing or has invalid headers, we cannot safely parse rows
            if (!workbook.TryGetWorksheet("Products", out var wsProducts) || sheetsWithInvalidHeaders.Contains("Products"))
            {
                var emptyResult = new ProductImportParsedWorkbook(
                    templateVersion,
                    0,
                    Array.Empty<ProductImportRowDraft>(),
                    parseErrors);
                return Task.FromResult(Result.Success(emptyResult));
            }

            // 6. Check Products row count <= 1,000 and collect non-blank rows
            var nonBlankProductRows = GetNonBlankRows(wsProducts, 10).ToList();
            if (nonBlankProductRows.Count > MaxProductRows)
            {
                return Task.FromResult(Result.Failure<ProductImportParsedWorkbook>(Error.Validation(
                    "ProductImport.TooManyRows",
                    $"Số lượng dòng sản phẩm ({nonBlankProductRows.Count}) vượt quá giới hạn tối đa {MaxProductRows} dòng.")));
            }


            if (nonBlankProductRows.Count == 0)
            {
                parseErrors.Add(new ProductImportErrorDto(
                    "Products",
                    2,
                    null,
                    null,
                    "ProductImport.NoProducts",
                    "Workbook phải chứa ít nhất một dòng sản phẩm."));
            }

            // 7. Check formulas across Products and specification sheets
            CheckFormulasAcrossSheets(workbook, parseErrors);

            // 8. Parse Products Sheet data
            var parsedProductManifest = ParseProductsSheet(nonBlankProductRows, parseErrors);

            // 9. Parse Specification Sheets data
            var parsedSpecs = ParseSpecificationSheets(workbook, sheetsWithInvalidHeaders, parseErrors);

            // 10. SKU Cross-Sheet Linking & Integrity
            var drafts = LinkProductsWithSpecifications(parsedProductManifest, parsedSpecs, parseErrors);

            var result = new ProductImportParsedWorkbook(
                templateVersion,
                nonBlankProductRows.Count,
                drafts,
                parseErrors);

            return Task.FromResult(Result.Success(result));
        }
    }

    #region ZIP Safety Check

    private static Result InspectZipSafety(Stream stream)
    {
        try
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

            if (archive.Entries.Count > MaxZipEntries)
            {
                return Result.Failure(Error.Validation(
                    "ProductImport.FileTooLarge",
                    $"Số lượng entry ({archive.Entries.Count}) vượt quá giới hạn tối đa {MaxZipEntries}."));
            }

            long totalUncompressedBytes = 0;
            foreach (var entry in archive.Entries)
            {
                var entryName = entry.FullName.Replace('\\', '/');

                // Macro / executable inspection
                if (entryName.Contains("vbaProject.bin", StringComparison.OrdinalIgnoreCase) ||
                    entryName.EndsWith(".xlsm", StringComparison.OrdinalIgnoreCase) ||
                    entryName.StartsWith("xl/vbaProject", StringComparison.OrdinalIgnoreCase))
                {
                    return Result.Failure(Error.Validation(
                        "ProductImport.MacroNotAllowed",
                        "File chứa macro hoặc mã thực thi (VBA) không được phép."));
                }

                // External links inspection
                if (entryName.Contains("externalLinks", StringComparison.OrdinalIgnoreCase) ||
                    entryName.Contains("externalLinkPath", StringComparison.OrdinalIgnoreCase))
                {
                    return Result.Failure(Error.Validation(
                        "ProductImport.InvalidZip",
                        "File chứa liên kết bên ngoài (external links) không được phép."));
                }

                totalUncompressedBytes += entry.Length;
                if (totalUncompressedBytes > MaxZipUncompressedBytes)
                {
                    return Result.Failure(Error.Validation(
                        "ProductImport.FileTooLarge",
                        $"Dung lượng giải nén vượt quá giới hạn tối đa {MaxZipUncompressedBytes / (1024 * 1024)} MiB."));
                }
            }

            return Result.Success();
        }
        catch (InvalidDataException)
        {
            return Result.Failure(Error.Validation(
                "ProductImport.CorruptedFile",
                "File không phải là định dạng ZIP/OpenXML hợp lệ hoặc dữ liệu bị hỏng."));
        }
        catch (Exception)
        {
            return Result.Failure(Error.Validation(
                "ProductImport.CorruptedFile",
                "Không thể kiểm tra cấu trúc file Excel. File bị hỏng hoặc không phải OpenXML hợp lệ."));
        }
    }

    #endregion

    #region Template Generation Helpers

    private static void SetupInstructionsSheet(IXLWorksheet ws)
    {
        ws.Cell("A1").Value = "HƯỚNG DẪN SỬ DỤNG WORKBOOK NHẬP LIỆU SẢN PHẨM RIUTEK";
        ws.Cell("A1").Style.Font.Bold = true;
        ws.Cell("A1").Style.Font.FontSize = 14;

        var instructions = new[]
        {
            "1. Sheet 'Products' là manifest chính. Mỗi dòng mô tả 1 sản phẩm.",
            "2. SKU là mã duy nhất và là khóa liên kết giữa sheet 'Products' và các sheet thông số kỹ thuật (CPU, GPU, RAM, ...).",
            "3. Mỗi sản phẩm trong 'Products' bắt buộc phải có đúng 1 dòng thông số tương ứng trong sheet khớp với 'ComponentType'.",
            "4. Có thể nhập một phần (ví dụ chỉ nhập CPU) hoặc trộn nhiều loại linh kiện trong cùng 1 file.",
            "5. Không xóa, đổi tên hoặc thay đổi thứ tự các cột đã định nghĩa sẵn.",
            "6. Các trường danh sách (ví dụ: SupportedSockets, SupportedFormFactors) dùng ký tự gạch đứng '|' để ngăn cách, ví dụ: AM4|AM5|LGA1700.",
            "7. ImageUrl bắt buộc là đường dẫn HTTPS hợp lệ. PrimaryImageFile là thông tin tham chiếu tương lai (có thể để trống).",
            "8. Các ô dữ liệu không được chứa công thức (formula).",
            "9. Các dòng hoàn toàn trống sẽ được tự động bỏ qua."
        };

        for (int i = 0; i < instructions.Length; i++)
        {
            ws.Cell(i + 3, 1).Value = instructions[i];
            ws.Cell(i + 3, 1).Style.Font.FontSize = 11;
        }

        ws.Columns(1, 2).AdjustToContents(15, 80);
    }

    private static void SetupDataSheet(
        IXLWorksheet ws,
        string sheetName,
        string[] headers,
        IReadOnlyList<CategoryLookupDto> categories)
    {
        for (int col = 0; col < headers.Length; col++)
        {
            var cell = ws.Cell(1, col + 1);
            cell.Value = headers[col];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8EEF5");
            cell.Style.Border.BottomBorder = XLBorderStyleValues.Medium;
        }

        ws.SheetView.FreezeRows(1);
        ws.Row(1).AsRange().SetAutoFilter();

        // Specific data validation rules
        if (sheetName == "Products")
        {
            // Col 3: CategorySlug (if categories exist)
            if (categories.Count > 0)
            {
                ws.Range("C2:C1001").CreateDataValidation().List($"='_Lookups'!$L$2:$L${categories.Count + 1}");
            }
            // Col 4: ComponentType
            var compTypesCount = Enum.GetNames<ComponentType>().Length;
            ws.Range("D2:D1001").CreateDataValidation().List($"='_Lookups'!$A$2:$A${compTypesCount + 1}");
        }
        else if (sheetName == "CPU")
        {
            var socketCount = Enum.GetNames<CpuSocket>().Length;
            ws.Range("B2:B1001").CreateDataValidation().List($"='_Lookups'!$B$2:$B${socketCount + 1}"); // Socket
            ws.Range("H2:H1001").CreateDataValidation().List("\"TRUE,FALSE\""); // HasIntegratedGpu
            var ramTypeCount = Enum.GetNames<RamType>().Length;
            ws.Range("I2:I1001").CreateDataValidation().List($"='_Lookups'!$C$2:$C${ramTypeCount + 1}"); // SupportedMemoryType
        }
        else if (sheetName == "Motherboard")
        {
            var socketCount = Enum.GetNames<CpuSocket>().Length;
            ws.Range("B2:B1001").CreateDataValidation().List($"='_Lookups'!$B$2:$B${socketCount + 1}"); // Socket
            var mbFfCount = Enum.GetNames<MotherboardFormFactor>().Length;
            ws.Range("D2:D1001").CreateDataValidation().List($"='_Lookups'!$E$2:$E${mbFfCount + 1}"); // FormFactor
            var ramTypeCount = Enum.GetNames<RamType>().Length;
            ws.Range("E2:E1001").CreateDataValidation().List($"='_Lookups'!$C$2:$C${ramTypeCount + 1}"); // MemoryType
            ws.Range("K2:K1001").CreateDataValidation().List("\"TRUE,FALSE\""); // HasWifi
        }
        else if (sheetName == "GPU")
        {
            ws.Range("I2:I1001").CreateDataValidation().List("\"TRUE,FALSE\""); // Requires12VHPWR
        }
        else if (sheetName == "RAM")
        {
            var ramTypeCount = Enum.GetNames<RamType>().Length;
            ws.Range("B2:B1001").CreateDataValidation().List($"='_Lookups'!$C$2:$C${ramTypeCount + 1}"); // MemoryType
            var ramFfCount = Enum.GetNames<RamFormFactor>().Length;
            ws.Range("C2:C1001").CreateDataValidation().List($"='_Lookups'!$D$2:$D${ramFfCount + 1}"); // FormFactor
            ws.Range("H2:H1001").CreateDataValidation().List("\"TRUE,FALSE\""); // HasRgb
        }
        else if (sheetName == "Storage")
        {
            var storageTypeCount = Enum.GetNames<StorageType>().Length;
            ws.Range("B2:B1001").CreateDataValidation().List($"='_Lookups'!$F$2:$F${storageTypeCount + 1}"); // StorageType
            var storageIfCount = Enum.GetNames<StorageInterface>().Length;
            ws.Range("C2:C1001").CreateDataValidation().List($"='_Lookups'!$G$2:$G${storageIfCount + 1}"); // Interface
        }
        else if (sheetName == "PSU")
        {
            var psuEffCount = Enum.GetNames<PsuEfficiency>().Length;
            ws.Range("C2:C1001").CreateDataValidation().List($"='_Lookups'!$H$2:$H${psuEffCount + 1}"); // Efficiency
            var psuModCount = Enum.GetNames<PsuModularity>().Length;
            ws.Range("D2:D1001").CreateDataValidation().List($"='_Lookups'!$I$2:$I${psuModCount + 1}"); // Modularity
            var psuFfCount = Enum.GetNames<PsuFormFactor>().Length;
            ws.Range("E2:E1001").CreateDataValidation().List($"='_Lookups'!$J$2:$J${psuFfCount + 1}"); // FormFactor
            ws.Range("F2:F1001").CreateDataValidation().List("\"TRUE,FALSE\""); // Has12VHPWR
        }
        else if (sheetName == "Cooler")
        {
            var coolerTypeCount = Enum.GetNames<CoolerType>().Length;
            ws.Range("B2:B1001").CreateDataValidation().List($"='_Lookups'!$K$2:$K${coolerTypeCount + 1}"); // CoolerType
            ws.Range("G2:G1001").CreateDataValidation().List("\"TRUE,FALSE\""); // HasRgb
        }

        ws.Columns(1, headers.Length).AdjustToContents(12, 35);
    }

    private static void SetupLookupsSheet(IXLWorksheet ws, IReadOnlyList<CategoryLookupDto> categories)
    {
        void WriteLookupColumn<TEnum>(int col, string header) where TEnum : struct, Enum
        {
            ws.Cell(1, col).Value = header;
            ws.Cell(1, col).Style.Font.Bold = true;
            var names = Enum.GetNames<TEnum>();
            for (int i = 0; i < names.Length; i++)
            {
                ws.Cell(i + 2, col).Value = names[i];
            }
        }

        WriteLookupColumn<ComponentType>(1, "ComponentType");
        WriteLookupColumn<CpuSocket>(2, "CpuSocket");
        WriteLookupColumn<RamType>(3, "RamType");
        WriteLookupColumn<RamFormFactor>(4, "RamFormFactor");
        WriteLookupColumn<MotherboardFormFactor>(5, "MotherboardFormFactor");
        WriteLookupColumn<StorageType>(6, "StorageType");
        WriteLookupColumn<StorageInterface>(7, "StorageInterface");
        WriteLookupColumn<PsuEfficiency>(8, "PsuEfficiency");
        WriteLookupColumn<PsuModularity>(9, "PsuModularity");
        WriteLookupColumn<PsuFormFactor>(10, "PsuFormFactor");
        WriteLookupColumn<CoolerType>(11, "CoolerType");

        // Categories
        ws.Cell(1, 12).Value = "CategorySlug";
        ws.Cell(1, 12).Style.Font.Bold = true;
        ws.Cell(1, 13).Value = "CategoryName";
        ws.Cell(1, 13).Style.Font.Bold = true;

        for (int i = 0; i < categories.Count; i++)
        {
            ws.Cell(i + 2, 12).Value = categories[i].Slug;
            ws.Cell(i + 2, 13).Value = categories[i].Name;
        }

        ws.Columns(1, 13).AdjustToContents(10, 30);
    }

    private static void SetupMetaSheet(IXLWorksheet ws)
    {
        ws.Cell("A1").Value = "Key";
        ws.Cell("B1").Value = "Value";
        ws.Row(1).Style.Font.Bold = true;

        ws.Cell("A2").Value = "TemplateName";
        ws.Cell("B2").Value = TemplateIdentifier;

        ws.Cell("A3").Value = "TemplateVersion";
        ws.Cell("B3").Value = CurrentTemplateVersion;

        ws.Cell("A4").Value = "ListSeparator";
        ws.Cell("B4").Value = DefaultListSeparator.ToString();
    }

    #endregion

    #region Parsing Helpers

    private static Dictionary<string, string> ParseMetaEntries(IXLWorksheet ws)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;

        for (int r = 2; r <= lastRow; r++)
        {
            var key = ws.Cell(r, 1).GetString()?.Trim();
            var val = ws.Cell(r, 2).GetString()?.Trim();
            if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(val))
            {
                dict[key] = val;
            }
        }

        return dict;
    }

    private static List<ProductImportErrorDto> ValidateHeaders(
        IXLWorksheet ws,
        string sheetName,
        string[] expectedHeaders)
    {
        var errors = new List<ProductImportErrorDto>();
        var lastCol = ws.Row(1).LastCellUsed()?.Address.ColumnNumber ?? 0;

        var actualHeaders = new List<string>();
        for (int c = 1; c <= lastCol; c++)
        {
            var val = ws.Cell(1, c).GetString()?.Trim();
            if (!string.IsNullOrEmpty(val))
            {
                actualHeaders.Add(val);
            }
        }

        var actualSet = new HashSet<string>(actualHeaders, StringComparer.Ordinal);
        var expectedSet = new HashSet<string>(expectedHeaders, StringComparer.Ordinal);

        // Missing headers
        foreach (var expected in expectedHeaders)
        {
            if (!actualSet.Contains(expected))
            {
                errors.Add(new ProductImportErrorDto(
                    sheetName,
                    1,
                    expected,
                    null,
                    "ProductImport.MissingColumn",
                    $"Cột bắt buộc '{expected}' bị thiếu trên sheet '{sheetName}'."));
            }
        }

        // Unexpected headers
        foreach (var actual in actualHeaders)
        {
            if (!expectedSet.Contains(actual))
            {
                errors.Add(new ProductImportErrorDto(
                    sheetName,
                    1,
                    actual,
                    null,
                    "ProductImport.UnexpectedColumn",
                    $"Cột '{actual}' không hợp lệ trên sheet '{sheetName}'."));
            }
        }

        // Exact order match (if no missing/unexpected headers)
        if (errors.Count == 0 && actualHeaders.Count == expectedHeaders.Length)
        {
            for (int i = 0; i < expectedHeaders.Length; i++)
            {
                if (!string.Equals(actualHeaders[i], expectedHeaders[i], StringComparison.Ordinal))
                {
                    errors.Add(new ProductImportErrorDto(
                        sheetName,
                        1,
                        actualHeaders[i],
                        null,
                        "ProductImport.MissingColumn",
                        $"Cột tại vị trí {i + 1} phải là '{expectedHeaders[i]}', nhưng thực tế là '{actualHeaders[i]}'."));
                    break;
                }
            }
        }

        return errors;
    }

    private static IEnumerable<IXLRow> GetNonBlankRows(IXLWorksheet ws, int maxCols)
    {
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
        for (int r = 2; r <= lastRow; r++)
        {
            var row = ws.Row(r);
            bool hasContent = false;
            for (int c = 1; c <= maxCols; c++)
            {
                var cell = row.Cell(c);
                if (!cell.IsEmpty() && !string.IsNullOrWhiteSpace(cell.GetString()))
                {
                    hasContent = true;
                    break;
                }
            }

            if (hasContent)
            {
                yield return row;
            }
        }
    }

    private static void CheckFormulasAcrossSheets(IXLWorkbook workbook, List<ProductImportErrorDto> errors)
    {
        var sheetsToCheck = new[] { "Products" }.Concat(SheetExpectedHeaders.Keys.Where(k => k != "Products"));

        foreach (var sheetName in sheetsToCheck)
        {
            if (!workbook.TryGetWorksheet(sheetName, out var ws)) continue;
            var usedRange = ws.RangeUsed();
            if (usedRange == null) continue;

            var lastRow = usedRange.LastRow().RowNumber();
            for (int r = 2; r <= lastRow; r++)
            {
                var row = ws.Row(r);
                if (row.IsEmpty()) continue;

                var skuCell = row.Cell(1);
                var sku = skuCell.IsEmpty() ? null : skuCell.GetString()?.Trim();

                var lastCol = row.LastCellUsed()?.Address.ColumnNumber ?? 0;
                for (int c = 1; c <= lastCol; c++)
                {
                    var cell = row.Cell(c);
                    if (cell.HasFormula)
                    {
                        var colHeader = ws.Cell(1, c).GetString()?.Trim();
                        errors.Add(new ProductImportErrorDto(
                            sheetName,
                            r,
                            colHeader,
                            sku,
                            "ProductImport.FormulaNotAllowed",
                            $"Ô tại hàng {r}, cột '{colHeader}' không được chứa công thức (formula)."));
                    }
                }
            }
        }
    }

    private record ParsedProductRowRaw(
        int RowNumber,
        string Sku,
        string Name,
        string CategorySlug,
        ComponentType? ComponentType,
        string Brand,
        decimal Price,
        decimal? OriginalPrice,
        int StockQuantity,
        string? PrimaryImageFile,
        string ImageUrl,
        bool HasFatalParseError);

    private static List<ParsedProductRowRaw> ParseProductsSheet(
        IEnumerable<IXLRow> rows,
        List<ProductImportErrorDto> errors)
    {
        var list = new List<ParsedProductRowRaw>();

        foreach (var row in rows)
        {
            var r = row.RowNumber();
            bool fatal = false;

            // 1. SKU
            var sku = row.Cell(1).GetString()?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(sku))
            {
                errors.Add(new ProductImportErrorDto("Products", r, "SKU", null, "ProductImport.MissingColumn", "Mã SKU không được để trống."));
                fatal = true;
            }

            // 2. Name
            var name = row.Cell(2).GetString()?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                errors.Add(new ProductImportErrorDto("Products", r, "Name", sku, "ProductImport.MissingColumn", "Tên sản phẩm không được để trống."));
                fatal = true;
            }

            // 3. CategorySlug
            var catSlug = row.Cell(3).GetString()?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(catSlug))
            {
                errors.Add(new ProductImportErrorDto("Products", r, "CategorySlug", sku, "ProductImport.MissingColumn", "CategorySlug không được để trống."));
                fatal = true;
            }

            // 4. ComponentType
            var compTypeStr = row.Cell(4).GetString()?.Trim() ?? string.Empty;
            ComponentType? compType = null;
            if (string.IsNullOrWhiteSpace(compTypeStr))
            {
                errors.Add(new ProductImportErrorDto("Products", r, "ComponentType", sku, "ProductImport.MissingColumn", "Loại linh kiện (ComponentType) không được để trống."));
                fatal = true;
            }
            else if (TryParseNamedEnum<ComponentType>(compTypeStr, out var parsedType))
            {
                compType = parsedType;
            }
            else
            {
                errors.Add(new ProductImportErrorDto("Products", r, "ComponentType", sku, "ProductImport.InvalidEnum", $"Loại linh kiện '{compTypeStr}' không hợp lệ."));
                fatal = true;
            }

            // 5. Brand
            var brand = row.Cell(5).GetString()?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(brand))
            {
                errors.Add(new ProductImportErrorDto("Products", r, "Brand", sku, "ProductImport.MissingColumn", "Thương hiệu không được để trống."));
                fatal = true;
            }

            // 6. Price
            decimal price = 0;
            var priceCell = row.Cell(6);
            if (priceCell.IsEmpty() || string.IsNullOrWhiteSpace(priceCell.GetString()))
            {
                errors.Add(new ProductImportErrorDto("Products", r, "Price", sku, "ProductImport.MissingColumn", "Giá sản phẩm không được để trống."));
                fatal = true;
            }
            else if (priceCell.DataType == XLDataType.Number)
            {
                var numericPrice = priceCell.GetDouble();
                if (!TryConvertToDecimal(numericPrice, out price))
                {
                    errors.Add(new ProductImportErrorDto("Products", r, "Price", sku, "ProductImport.InvalidCellType", "Giá sản phẩm vượt ngoài phạm vi số hợp lệ."));
                    fatal = true;
                }
            }
            else if (!decimal.TryParse(priceCell.GetString()?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out price))
            {
                errors.Add(new ProductImportErrorDto("Products", r, "Price", sku, "ProductImport.InvalidCellType", "Giá sản phẩm phải là số hợp lệ."));
                fatal = true;
            }

            // 7. OriginalPrice
            decimal? origPrice = null;
            var origPriceCell = row.Cell(7);
            if (!origPriceCell.IsEmpty() && !string.IsNullOrWhiteSpace(origPriceCell.GetString()))
            {
                if (origPriceCell.DataType == XLDataType.Number)
                {
                    var numericOriginalPrice = origPriceCell.GetDouble();
                    if (TryConvertToDecimal(numericOriginalPrice, out var convertedOriginalPrice))
                    {
                        origPrice = convertedOriginalPrice;
                    }
                    else
                    {
                        errors.Add(new ProductImportErrorDto("Products", r, "OriginalPrice", sku, "ProductImport.InvalidCellType", "Giá gốc sản phẩm vượt ngoài phạm vi số hợp lệ."));
                        fatal = true;
                    }
                }
                else if (decimal.TryParse(origPriceCell.GetString()?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedOrig))
                {
                    origPrice = parsedOrig;
                }
                else
                {
                    errors.Add(new ProductImportErrorDto("Products", r, "OriginalPrice", sku, "ProductImport.InvalidCellType", "Giá gốc sản phẩm phải là số hợp lệ."));
                    fatal = true;
                }
            }

            // 8. StockQuantity
            int stock = 0;
            var stockCell = row.Cell(8);
            if (stockCell.IsEmpty() || string.IsNullOrWhiteSpace(stockCell.GetString()))
            {
                errors.Add(new ProductImportErrorDto("Products", r, "StockQuantity", sku, "ProductImport.MissingColumn", "Số lượng tồn kho không được để trống."));
                fatal = true;
            }
            else if (stockCell.DataType == XLDataType.Number)
            {
                var dVal = stockCell.GetDouble();
                if (!double.IsFinite(dVal) ||
                    dVal < int.MinValue ||
                    dVal > int.MaxValue ||
                    Math.Abs(dVal - Math.Round(dVal)) > 0.0001)
                {
                    errors.Add(new ProductImportErrorDto("Products", r, "StockQuantity", sku, "ProductImport.InvalidCellType", "Số lượng tồn kho phải là số nguyên hợp lệ."));
                    fatal = true;
                }
                else
                {
                    stock = (int)Math.Round(dVal);
                }
            }
            else if (!int.TryParse(stockCell.GetString()?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out stock))
            {
                errors.Add(new ProductImportErrorDto("Products", r, "StockQuantity", sku, "ProductImport.InvalidCellType", "Số lượng tồn kho phải là số nguyên hợp lệ."));
                fatal = true;
            }

            // 9. PrimaryImageFile
            var primaryImageFile = row.Cell(9).GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(primaryImageFile)) primaryImageFile = null;

            // 10. ImageUrl
            var imageUrl = row.Cell(10).GetString()?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                errors.Add(new ProductImportErrorDto("Products", r, "ImageUrl", sku, "ProductImport.MissingColumn", "Đường dẫn ảnh chính (ImageUrl) không được để trống."));
                fatal = true;
            }
            else if (!ProductMediaHelper.IsValidHttpsUrl(imageUrl))
            {
                errors.Add(new ProductImportErrorDto("Products", r, "ImageUrl", sku, "ProductImport.InvalidImageUrl", "Ảnh sản phẩm chính phải là đường dẫn HTTPS hợp lệ."));
                fatal = true;
            }

            list.Add(new ParsedProductRowRaw(
                r,
                sku,
                name,
                catSlug,
                compType,
                brand,
                price,
                origPrice,
                stock,
                primaryImageFile,
                imageUrl,
                fatal));
        }

        return list;
    }

    private record ParsedSpecRow(
        string SheetName,
        int RowNumber,
        string Sku,
        ComponentSpecification? Specification,
        bool HasFatalParseError);

    private static List<ParsedSpecRow> ParseSpecificationSheets(
        IXLWorkbook workbook,
        HashSet<string> sheetsWithInvalidHeaders,
        List<ProductImportErrorDto> errors)
    {
        var specRows = new List<ParsedSpecRow>();

        var specSheetNames = SheetExpectedHeaders.Keys.Where(k => k != "Products");
        foreach (var sheetName in specSheetNames)
        {
            if (sheetsWithInvalidHeaders.Contains(sheetName)) continue;
            if (!workbook.TryGetWorksheet(sheetName, out var ws)) continue;

            var expectedCols = SheetExpectedHeaders[sheetName].Length;
            var nonBlankRows = GetNonBlankRows(ws, expectedCols);

            foreach (var row in nonBlankRows)
            {
                var r = row.RowNumber();
                var sku = row.Cell(1).GetString()?.Trim() ?? string.Empty;

                if (string.IsNullOrWhiteSpace(sku))
                {
                    errors.Add(new ProductImportErrorDto(
                        sheetName,
                        r,
                        "SKU",
                        null,
                        "ProductImport.MissingColumn",
                        $"Mã SKU không được để trống trong sheet '{sheetName}'."));
                    specRows.Add(new ParsedSpecRow(sheetName, r, sku, null, true));
                    continue;
                }

                ComponentSpecification? spec = null;
                bool fatal = false;

                try
                {
                    spec = sheetName switch
                    {
                        "CPU" => ParseCpuSpec(row, sheetName, r, sku, errors, ref fatal),
                        "Motherboard" => ParseMotherboardSpec(row, sheetName, r, sku, errors, ref fatal),
                        "GPU" => ParseGpuSpec(row, sheetName, r, sku, errors, ref fatal),
                        "RAM" => ParseRamSpec(row, sheetName, r, sku, errors, ref fatal),
                        "Storage" => ParseStorageSpec(row, sheetName, r, sku, errors, ref fatal),
                        "PSU" => ParsePsuSpec(row, sheetName, r, sku, errors, ref fatal),
                        "Case" => ParseCaseSpec(row, sheetName, r, sku, errors, ref fatal),
                        "Cooler" => ParseCoolerSpec(row, sheetName, r, sku, errors, ref fatal),
                        "Accessory" => ParseAccessorySpec(row, sheetName, r, sku, errors, ref fatal),
                        _ => null
                    };
                }
                catch (Exception)
                {
                    errors.Add(new ProductImportErrorDto(
                        sheetName,
                        r,
                        null,
                        sku,
                        "ProductImport.InvalidCellType",
                        "Không thể đọc dữ liệu thông số trong dòng này."));
                    fatal = true;
                }

                specRows.Add(new ParsedSpecRow(sheetName, r, sku, spec, fatal));
            }
        }

        return specRows;
    }

    #region Spec Parsers per Component

    private static CpuSpecification? ParseCpuSpec(
        IXLRow row, string sheet, int r, string sku, List<ProductImportErrorDto> errors, ref bool fatal)
    {
        var socket = ParseEnumCell<CpuSocket>(row.Cell(2), sheet, r, "Socket", sku, errors, ref fatal);
        var coreCount = ParseIntCell(row.Cell(3), sheet, r, "CoreCount", sku, errors, ref fatal);
        var threadCount = ParseIntCell(row.Cell(4), sheet, r, "ThreadCount", sku, errors, ref fatal);
        var baseClock = ParseDoubleCell(row.Cell(5), sheet, r, "BaseClockGhz", sku, errors, ref fatal);
        var boostClock = ParseDoubleCell(row.Cell(6), sheet, r, "BoostClockGhz", sku, errors, ref fatal);
        var tdp = ParseIntCell(row.Cell(7), sheet, r, "TdpWattage", sku, errors, ref fatal);
        var igpu = ParseBoolCell(row.Cell(8), sheet, r, "HasIntegratedGpu", sku, errors, ref fatal);
        var memType = ParseEnumCell<RamType>(row.Cell(9), sheet, r, "SupportedMemoryType", sku, errors, ref fatal);
        var maxMemSpeed = ParseIntCell(row.Cell(10), sheet, r, "MaxMemorySpeedMhz", sku, errors, ref fatal);

        if (fatal) return null;

        return new CpuSpecification
        {
            Socket = socket,
            CoreCount = coreCount,
            ThreadCount = threadCount,
            BaseClockGhz = baseClock,
            BoostClockGhz = boostClock,
            TdpWattage = tdp,
            HasIntegratedGpu = igpu,
            SupportedMemoryType = memType,
            MaxMemorySpeedMhz = maxMemSpeed
        };
    }

    private static MotherboardSpecification? ParseMotherboardSpec(
        IXLRow row, string sheet, int r, string sku, List<ProductImportErrorDto> errors, ref bool fatal)
    {
        var socket = ParseEnumCell<CpuSocket>(row.Cell(2), sheet, r, "Socket", sku, errors, ref fatal);
        var chipset = row.Cell(3).GetString()?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(chipset))
        {
            errors.Add(new ProductImportErrorDto(sheet, r, "Chipset", sku, "ProductImport.MissingColumn", "Chipset không được để trống."));
            fatal = true;
        }

        var formFactor = ParseEnumCell<MotherboardFormFactor>(row.Cell(4), sheet, r, "FormFactor", sku, errors, ref fatal);
        var memType = ParseEnumCell<RamType>(row.Cell(5), sheet, r, "MemoryType", sku, errors, ref fatal);
        var memSlots = ParseIntCell(row.Cell(6), sheet, r, "MemorySlots", sku, errors, ref fatal);
        var maxCap = ParseIntCell(row.Cell(7), sheet, r, "MaxMemoryCapacityGb", sku, errors, ref fatal);
        var m2Slots = ParseIntCell(row.Cell(8), sheet, r, "M2Slots", sku, errors, ref fatal);
        var sataPorts = ParseIntCell(row.Cell(9), sheet, r, "SataPorts", sku, errors, ref fatal);
        var pcieSlots = ParseIntCell(row.Cell(10), sheet, r, "PcieSlots", sku, errors, ref fatal);
        var wifi = ParseBoolCell(row.Cell(11), sheet, r, "HasWifi", sku, errors, ref fatal);

        if (fatal) return null;

        return new MotherboardSpecification
        {
            Socket = socket,
            Chipset = chipset,
            FormFactor = formFactor,
            MemoryType = memType,
            MemorySlots = memSlots,
            MaxMemoryCapacityGb = maxCap,
            M2Slots = m2Slots,
            SataPorts = sataPorts,
            PcieSlots = pcieSlots,
            HasWifi = wifi
        };
    }

    private static GpuSpecification? ParseGpuSpec(
        IXLRow row, string sheet, int r, string sku, List<ProductImportErrorDto> errors, ref bool fatal)
    {
        var chipset = row.Cell(2).GetString()?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(chipset))
        {
            errors.Add(new ProductImportErrorDto(sheet, r, "Chipset", sku, "ProductImport.MissingColumn", "Chipset không được để trống."));
            fatal = true;
        }

        var vramGb = ParseIntCell(row.Cell(3), sheet, r, "VramGb", sku, errors, ref fatal);
        var vramType = row.Cell(4).GetString()?.Trim() ?? "GDDR6";
        var length = ParseIntCell(row.Cell(5), sheet, r, "LengthMm", sku, errors, ref fatal);
        var slotWidth = ParseDoubleCell(row.Cell(6), sheet, r, "SlotWidth", sku, errors, ref fatal);
        var tdp = ParseIntCell(row.Cell(7), sheet, r, "TdpWattage", sku, errors, ref fatal);
        var recPsu = ParseIntCell(row.Cell(8), sheet, r, "RecommendedPsuWattage", sku, errors, ref fatal);
        var req12V = ParseBoolCell(row.Cell(9), sheet, r, "Requires12VHPWR", sku, errors, ref fatal);
        var pwrConn = row.Cell(10).GetString()?.Trim() ?? string.Empty;

        if (fatal) return null;

        return new GpuSpecification
        {
            Chipset = chipset,
            VramGb = vramGb,
            VramType = vramType,
            LengthMm = length,
            SlotWidth = slotWidth,
            TdpWattage = tdp,
            RecommendedPsuWattage = recPsu,
            Requires12VHPWR = req12V,
            PowerConnectors = pwrConn
        };
    }

    private static RamSpecification? ParseRamSpec(
        IXLRow row, string sheet, int r, string sku, List<ProductImportErrorDto> errors, ref bool fatal)
    {
        var memType = ParseEnumCell<RamType>(row.Cell(2), sheet, r, "MemoryType", sku, errors, ref fatal);
        var formFactor = ParseEnumCell<RamFormFactor>(row.Cell(3), sheet, r, "FormFactor", sku, errors, ref fatal);
        var capGb = ParseIntCell(row.Cell(4), sheet, r, "CapacityGb", sku, errors, ref fatal);
        var modCount = ParseIntCell(row.Cell(5), sheet, r, "ModuleCount", sku, errors, ref fatal);
        var speed = ParseIntCell(row.Cell(6), sheet, r, "SpeedMhz", sku, errors, ref fatal);
        var cas = ParseIntCell(row.Cell(7), sheet, r, "CasLatency", sku, errors, ref fatal);
        var rgb = ParseBoolCell(row.Cell(8), sheet, r, "HasRgb", sku, errors, ref fatal);

        if (fatal) return null;

        return new RamSpecification
        {
            MemoryType = memType,
            FormFactor = formFactor,
            CapacityGb = capGb,
            ModuleCount = modCount,
            SpeedMhz = speed,
            CasLatency = cas,
            HasRgb = rgb
        };
    }

    private static StorageSpecification? ParseStorageSpec(
        IXLRow row, string sheet, int r, string sku, List<ProductImportErrorDto> errors, ref bool fatal)
    {
        var stType = ParseEnumCell<StorageType>(row.Cell(2), sheet, r, "StorageType", sku, errors, ref fatal);
        var stIf = ParseEnumCell<StorageInterface>(row.Cell(3), sheet, r, "Interface", sku, errors, ref fatal);
        var capGb = ParseIntCell(row.Cell(4), sheet, r, "CapacityGb", sku, errors, ref fatal);
        var readSpeed = ParseIntCell(row.Cell(5), sheet, r, "ReadSpeedMBs", sku, errors, ref fatal);
        var writeSpeed = ParseIntCell(row.Cell(6), sheet, r, "WriteSpeedMBs", sku, errors, ref fatal);
        var formFactor = row.Cell(7).GetString()?.Trim() ?? "M.2 2280";

        if (fatal) return null;

        return new StorageSpecification
        {
            StorageType = stType,
            Interface = stIf,
            CapacityGb = capGb,
            ReadSpeedMBs = readSpeed,
            WriteSpeedMBs = writeSpeed,
            FormFactor = formFactor
        };
    }

    private static PsuSpecification? ParsePsuSpec(
        IXLRow row, string sheet, int r, string sku, List<ProductImportErrorDto> errors, ref bool fatal)
    {
        var wattage = ParseIntCell(row.Cell(2), sheet, r, "Wattage", sku, errors, ref fatal);
        var efficiency = ParseEnumCell<PsuEfficiency>(row.Cell(3), sheet, r, "Efficiency", sku, errors, ref fatal);
        var modularity = ParseEnumCell<PsuModularity>(row.Cell(4), sheet, r, "Modularity", sku, errors, ref fatal);
        var formFactor = ParseEnumCell<PsuFormFactor>(row.Cell(5), sheet, r, "FormFactor", sku, errors, ref fatal);
        var has12V = ParseBoolCell(row.Cell(6), sheet, r, "Has12VHPWR", sku, errors, ref fatal);
        var length = ParseIntCell(row.Cell(7), sheet, r, "LengthMm", sku, errors, ref fatal);

        if (fatal) return null;

        return new PsuSpecification
        {
            Wattage = wattage,
            Efficiency = efficiency,
            Modularity = modularity,
            FormFactor = formFactor,
            Has12VHPWR = has12V,
            LengthMm = length
        };
    }

    private static CaseSpecification? ParseCaseSpec(
        IXLRow row, string sheet, int r, string sku, List<ProductImportErrorDto> errors, ref bool fatal)
    {
        var formFactors = ParseEnumListCell<MotherboardFormFactor>(row.Cell(2), sheet, r, "SupportedFormFactors", sku, errors, ref fatal);
        var maxGpu = ParseIntCell(row.Cell(3), sheet, r, "MaxGpuLengthMm", sku, errors, ref fatal);
        var maxCooler = ParseIntCell(row.Cell(4), sheet, r, "MaxCpuCoolerHeightMm", sku, errors, ref fatal);
        var maxPsu = ParseIntCell(row.Cell(5), sheet, r, "MaxPsuLengthMm", sku, errors, ref fatal);
        var radiators = ParseIntListCell(row.Cell(6), sheet, r, "SupportedRadiatorSizesMm", sku, errors, ref fatal, allowEmpty: true);
        var fans = ParseIntCell(row.Cell(7), sheet, r, "IncludedFans", sku, errors, ref fatal);

        if (fatal) return null;

        return new CaseSpecification
        {
            SupportedFormFactors = formFactors,
            MaxGpuLengthMm = maxGpu,
            MaxCpuCoolerHeightMm = maxCooler,
            MaxPsuLengthMm = maxPsu,
            SupportedRadiatorSizesMm = radiators,
            IncludedFans = fans
        };
    }

    private static CoolerSpecification? ParseCoolerSpec(
        IXLRow row, string sheet, int r, string sku, List<ProductImportErrorDto> errors, ref bool fatal)
    {
        var coolerType = ParseEnumCell<CoolerType>(row.Cell(2), sheet, r, "CoolerType", sku, errors, ref fatal);
        var sockets = ParseEnumListCell<CpuSocket>(row.Cell(3), sheet, r, "SupportedSockets", sku, errors, ref fatal);
        var height = ParseIntCell(row.Cell(4), sheet, r, "HeightMm", sku, errors, ref fatal);
        var radSize = ParseIntCell(row.Cell(5), sheet, r, "RadiatorSizeMm", sku, errors, ref fatal);
        var maxTdp = ParseIntCell(row.Cell(6), sheet, r, "MaxTdpRating", sku, errors, ref fatal);
        var rgb = ParseBoolCell(row.Cell(7), sheet, r, "HasRgb", sku, errors, ref fatal);

        if (fatal) return null;

        return new CoolerSpecification
        {
            CoolerType = coolerType,
            SupportedSockets = sockets,
            HeightMm = height,
            RadiatorSizeMm = radSize,
            MaxTdpRating = maxTdp,
            HasRgb = rgb
        };
    }

    private static AccessorySpecification? ParseAccessorySpec(
        IXLRow row, string sheet, int r, string sku, List<ProductImportErrorDto> errors, ref bool fatal)
    {
        var details = row.Cell(2).GetString()?.Trim() ?? string.Empty;
        return new AccessorySpecification
        {
            Details = details
        };
    }

    #endregion

    #region Cell Value Parsing Helpers

    private static TEnum ParseEnumCell<TEnum>(
        IXLCell cell, string sheet, int r, string col, string sku, List<ProductImportErrorDto> errors, ref bool fatal)
        where TEnum : struct, Enum
    {
        var str = cell.GetString()?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(str))
        {
            errors.Add(new ProductImportErrorDto(sheet, r, col, sku, "ProductImport.MissingColumn", $"Cột '{col}' không được để trống."));
            fatal = true;
            return default;
        }

        if (TryParseNamedEnum<TEnum>(str, out var val))
        {
            return val;
        }

        errors.Add(new ProductImportErrorDto(sheet, r, col, sku, "ProductImport.InvalidEnum", $"Giá trị '{str}' không hợp lệ cho cột '{col}'."));
        fatal = true;
        return default;
    }

    private static int ParseIntCell(
        IXLCell cell, string sheet, int r, string col, string sku, List<ProductImportErrorDto> errors, ref bool fatal)
    {
        if (cell.IsEmpty() || string.IsNullOrWhiteSpace(cell.GetString()))
        {
            errors.Add(new ProductImportErrorDto(sheet, r, col, sku, "ProductImport.MissingColumn", $"Cột '{col}' không được để trống."));
            fatal = true;
            return 0;
        }

        if (cell.DataType == XLDataType.Number)
        {
            var dVal = cell.GetDouble();
            if (!double.IsFinite(dVal) ||
                dVal < int.MinValue ||
                dVal > int.MaxValue ||
                Math.Abs(dVal - Math.Round(dVal)) > 0.0001)
            {
                errors.Add(new ProductImportErrorDto(sheet, r, col, sku, "ProductImport.InvalidCellType", $"Giá trị cột '{col}' phải là số nguyên hợp lệ."));
                fatal = true;
                return 0;
            }
            return checked((int)Math.Round(dVal));
        }

        if (int.TryParse(cell.GetString()?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        errors.Add(new ProductImportErrorDto(sheet, r, col, sku, "ProductImport.InvalidCellType", $"Giá trị cột '{col}' phải là số nguyên hợp lệ."));
        fatal = true;
        return 0;
    }

    private static double ParseDoubleCell(
        IXLCell cell, string sheet, int r, string col, string sku, List<ProductImportErrorDto> errors, ref bool fatal)
    {
        if (cell.IsEmpty() || string.IsNullOrWhiteSpace(cell.GetString()))
        {
            errors.Add(new ProductImportErrorDto(sheet, r, col, sku, "ProductImport.MissingColumn", $"Cột '{col}' không được để trống."));
            fatal = true;
            return 0;
        }

        if (cell.DataType == XLDataType.Number)
        {
            var value = cell.GetDouble();
            if (double.IsFinite(value))
            {
                return value;
            }

            errors.Add(new ProductImportErrorDto(sheet, r, col, sku, "ProductImport.InvalidCellType", $"Giá trị cột '{col}' phải là số hữu hạn."));
            fatal = true;
            return 0;
        }

        if (double.TryParse(cell.GetString()?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
            double.IsFinite(parsed))
        {
            return parsed;
        }

        errors.Add(new ProductImportErrorDto(sheet, r, col, sku, "ProductImport.InvalidCellType", $"Giá trị cột '{col}' phải là số thực hợp lệ."));
        fatal = true;
        return 0;
    }

    private static bool ParseBoolCell(
        IXLCell cell, string sheet, int r, string col, string sku, List<ProductImportErrorDto> errors, ref bool fatal)
    {
        if (cell.IsEmpty() || string.IsNullOrWhiteSpace(cell.GetString()))
        {
            errors.Add(new ProductImportErrorDto(sheet, r, col, sku, "ProductImport.MissingColumn", $"Cột '{col}' không được để trống."));
            fatal = true;
            return false;
        }

        if (cell.DataType == XLDataType.Boolean)
        {
            return cell.GetBoolean();
        }

        var str = cell.GetString()?.Trim();
        if (string.Equals(str, "TRUE", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(str, "FALSE", StringComparison.OrdinalIgnoreCase)) return false;

        errors.Add(new ProductImportErrorDto(sheet, r, col, sku, "ProductImport.InvalidCellType", $"Giá trị cột '{col}' phải là TRUE hoặc FALSE."));
        fatal = true;
        return false;
    }

    private static List<TEnum> ParseEnumListCell<TEnum>(
        IXLCell cell, string sheet, int r, string col, string sku, List<ProductImportErrorDto> errors, ref bool fatal)
        where TEnum : struct, Enum
    {
        var str = cell.GetString()?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(str))
        {
            errors.Add(new ProductImportErrorDto(sheet, r, col, sku, "ProductImport.MissingColumn", $"Cột '{col}' không được để trống."));
            fatal = true;
            return [];
        }

        var rawParts = str.Split(DefaultListSeparator);
        var list = new List<TEnum>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in rawParts)
        {
            var part = raw.Trim();
            if (string.IsNullOrEmpty(part))
            {
                errors.Add(new ProductImportErrorDto(sheet, r, col, sku, "ProductImport.InvalidCellType", $"Danh sách cột '{col}' chứa phần tử rỗng."));
                fatal = true;
                continue;
            }

            if (!seen.Add(part))
            {
                errors.Add(new ProductImportErrorDto(sheet, r, col, sku, "ProductImport.InvalidCellType", $"Danh sách cột '{col}' chứa phần tử trùng lặp: '{part}'."));
                fatal = true;
                continue;
            }

            if (TryParseNamedEnum<TEnum>(part, out var parsed))
            {
                list.Add(parsed);
            }
            else
            {
                errors.Add(new ProductImportErrorDto(sheet, r, col, sku, "ProductImport.InvalidEnum", $"Giá trị '{part}' trong danh sách cột '{col}' không hợp lệ."));
                fatal = true;
            }
        }

        return list;
    }

    private static List<int> ParseIntListCell(
        IXLCell cell,
        string sheet,
        int r,
        string col,
        string sku,
        List<ProductImportErrorDto> errors,
        ref bool fatal,
        bool allowEmpty = false)
    {
        var str = cell.GetString()?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(str))
        {
            if (allowEmpty)
            {
                return [];
            }

            errors.Add(new ProductImportErrorDto(sheet, r, col, sku, "ProductImport.MissingColumn", $"Cột '{col}' không được để trống."));
            fatal = true;
            return [];
        }

        var rawParts = str.Split(DefaultListSeparator);
        var list = new List<int>();
        var seen = new HashSet<int>();

        foreach (var raw in rawParts)
        {
            var part = raw.Trim();
            if (string.IsNullOrEmpty(part))
            {
                errors.Add(new ProductImportErrorDto(sheet, r, col, sku, "ProductImport.InvalidCellType", $"Danh sách cột '{col}' chứa phần tử rỗng."));
                fatal = true;
                continue;
            }

            if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var num))
            {
                errors.Add(new ProductImportErrorDto(sheet, r, col, sku, "ProductImport.InvalidCellType", $"Giá trị '{part}' trong danh sách cột '{col}' phải là số nguyên hợp lệ."));
                fatal = true;
                continue;
            }

            if (!seen.Add(num))
            {
                errors.Add(new ProductImportErrorDto(sheet, r, col, sku, "ProductImport.InvalidCellType", $"Danh sách cột '{col}' chứa phần tử trùng lặp: '{num}'."));
                fatal = true;
                continue;
            }

            list.Add(num);
        }

        return list;
    }

    private static bool TryParseNamedEnum<TEnum>(string value, out TEnum parsed)
        where TEnum : struct, Enum
    {
        parsed = default;

        if (string.IsNullOrWhiteSpace(value) ||
            !Enum.GetNames<TEnum>().Contains(value, StringComparer.Ordinal))
        {
            return false;
        }

        return Enum.TryParse(value, ignoreCase: false, out parsed) && Enum.IsDefined(parsed);
    }

    private static bool TryConvertToDecimal(double value, out decimal result)
    {
        result = default;
        if (!double.IsFinite(value) || value < (double)decimal.MinValue || value > (double)decimal.MaxValue)
        {
            return false;
        }

        result = (decimal)value;
        return true;
    }

    #endregion

    #endregion

    #region Cross-Sheet Linking

    private static List<ProductImportRowDraft> LinkProductsWithSpecifications(
        List<ParsedProductRowRaw> products,
        List<ParsedSpecRow> specs,
        List<ProductImportErrorDto> errors)
    {
        var drafts = new List<ProductImportRowDraft>();

        // Index products by SKU (trimmed upper)
        var productSkuMap = products
            .Where(p => !string.IsNullOrWhiteSpace(p.Sku))
            .GroupBy(p => p.Sku.Trim().ToUpperInvariant(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        // Group specs by SKU and sheet
        var specSkuSheetMap = new Dictionary<string, List<ParsedSpecRow>>(StringComparer.OrdinalIgnoreCase);
        foreach (var spec in specs)
        {
            if (string.IsNullOrWhiteSpace(spec.Sku)) continue;
            var key = spec.Sku.Trim().ToUpperInvariant();
            if (!specSkuSheetMap.TryGetValue(key, out var list))
            {
                list = [];
                specSkuSheetMap[key] = list;
            }
            list.Add(spec);
        }

        // 1. Inspect specs: Orphan specs, Type mismatch, Duplicate specs
        foreach (var spec in specs)
        {
            if (string.IsNullOrWhiteSpace(spec.Sku)) continue;
            var skuKey = spec.Sku.Trim().ToUpperInvariant();

            if (!productSkuMap.TryGetValue(skuKey, out var matchedProducts))
            {
                // Orphan spec
                errors.Add(new ProductImportErrorDto(
                    spec.SheetName,
                    spec.RowNumber,
                    "SKU",
                    spec.Sku,
                    "ProductImport.OrphanSpecification",
                    $"Mã SKU '{spec.Sku}' trong sheet '{spec.SheetName}' không tồn tại trong sheet 'Products'."));
                continue;
            }

            var matchedProduct = matchedProducts.First();
            if (matchedProduct.ComponentType.HasValue)
            {
                var expectedSheet = matchedProduct.ComponentType.Value.ToString();
                if (!string.Equals(spec.SheetName, expectedSheet, StringComparison.OrdinalIgnoreCase))
                {
                    // Specification Type Mismatch
                    errors.Add(new ProductImportErrorDto(
                        spec.SheetName,
                        spec.RowNumber,
                        "SKU",
                        spec.Sku,
                        "ProductImport.SpecificationTypeMismatch",
                        $"Sản phẩm có SKU '{spec.Sku}' có ComponentType là '{matchedProduct.ComponentType}', không khớp với sheet '{spec.SheetName}'."));
                }
            }
        }

        // Duplicate spec rows within or across sheets
        foreach (var kvp in specSkuSheetMap)
        {
            if (kvp.Value.Count > 1)
            {
                foreach (var dupSpec in kvp.Value.Skip(1))
                {
                    errors.Add(new ProductImportErrorDto(
                        dupSpec.SheetName,
                        dupSpec.RowNumber,
                        "SKU",
                        dupSpec.Sku,
                        "ProductImport.DuplicateSku",
                        $"Mã SKU '{dupSpec.Sku}' bị lặp lại trong sheet '{dupSpec.SheetName}'."));
                }
            }
        }

        // 2. Inspect products: Missing specifications, Assemble drafts
        foreach (var prod in products)
        {
            if (string.IsNullOrWhiteSpace(prod.Sku) || !prod.ComponentType.HasValue)
            {
                continue;
            }

            var skuKey = prod.Sku.Trim().ToUpperInvariant();
            var expectedSheet = prod.ComponentType.Value.ToString();

            ParsedSpecRow? matchedSpec = null;
            if (specSkuSheetMap.TryGetValue(skuKey, out var specList))
            {
                matchedSpec = specList.FirstOrDefault(s => string.Equals(s.SheetName, expectedSheet, StringComparison.OrdinalIgnoreCase));
            }

            if (matchedSpec == null)
            {
                errors.Add(new ProductImportErrorDto(
                    "Products",
                    prod.RowNumber,
                    "SKU",
                    prod.Sku,
                    "ProductImport.MissingSpecification",
                    $"Sản phẩm có SKU '{prod.Sku}' thiếu thông số kỹ thuật trong sheet '{expectedSheet}'."));
            }
            else
            {
                drafts.Add(new ProductImportRowDraft(
                    prod.RowNumber,
                    prod.Sku,
                    prod.Name,
                    prod.CategorySlug,
                    prod.ComponentType.Value,
                    prod.Brand,
                    prod.Price,
                    prod.OriginalPrice,
                    prod.StockQuantity,
                    prod.PrimaryImageFile,
                    prod.ImageUrl,
                    matchedSpec.Specification,
                    matchedSpec.RowNumber,
                    matchedSpec.SheetName));
            }
        }

        return drafts;
    }

    #endregion
}

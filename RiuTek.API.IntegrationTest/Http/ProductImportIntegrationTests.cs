using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RiuTek.API.IntegrationTest.Infrastructure;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Core.Entities;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;
using RiuTek.Infrastructure.Data;
using Xunit;

namespace RiuTek.API.IntegrationTest.Http;

[Collection(IntegrationTestCollection.Name)]
public class ProductImportIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainerFixture _fixture;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public ProductImportIntegrationTests(PostgreSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync() => await CleanupDataAsync();
    public async Task DisposeAsync() => await CleanupDataAsync();

    private async Task CleanupDataAsync()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            await db.Products.ExecuteDeleteAsync();
            await db.Categories.ExecuteDeleteAsync();
            await db.Users.ExecuteDeleteAsync();
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    private async Task<string> SeedUserAndGenerateTokenAsync(UserRole role)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var jwtGenerator = scope.ServiceProvider.GetRequiredService<IJwtTokenGenerator>();

        var user = new User(
            email: $"import_{role.ToString().ToLowerInvariant()}_{Guid.NewGuid():N}@riutek.test",
            passwordHash: "dummy_hashed_password",
            fullName: $"{role} Import User",
            role: role
        ) { IsActive = true };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        return jwtGenerator.GenerateAccessToken(user);
    }

    private async Task<HttpClient> CreateClientForRoleAsync(UserRole? role = UserRole.Admin)
    {
        var client = _fixture.Factory.CreateClient();
        if (role.HasValue)
        {
            var token = await SeedUserAndGenerateTokenAsync(role.Value);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return client;
    }

    private async Task<byte[]> CreateCpuWorkbookAsync(
        string sku,
        string name,
        bool includeSpecification = true)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var workbookService = scope.ServiceProvider.GetRequiredService<IProductImportWorkbookService>();
        var template = await workbookService.GenerateTemplateAsync(
            [new("cpu", "Vi xử lý", ComponentType.Cpu)]);

        using var input = new MemoryStream(template.Content);
        using var workbook = new XLWorkbook(input);
        var products = workbook.Worksheet("Products");
        products.Cell(2, 1).Value = sku;
        products.Cell(2, 2).Value = name;
        products.Cell(2, 3).Value = "cpu";
        products.Cell(2, 4).Value = "Cpu";
        products.Cell(2, 5).Value = "AMD";
        products.Cell(2, 6).Value = 5_000_000;
        products.Cell(2, 7).Value = 5_500_000;
        products.Cell(2, 8).Value = 10;
        products.Cell(2, 10).Value = "https://media.riutek.test/cpu.webp";

        if (includeSpecification)
        {
            var cpu = workbook.Worksheet("CPU");
            cpu.Cell(2, 1).Value = sku;
            cpu.Cell(2, 2).Value = "AM5";
            cpu.Cell(2, 3).Value = 6;
            cpu.Cell(2, 4).Value = 12;
            cpu.Cell(2, 5).Value = 3.8;
            cpu.Cell(2, 6).Value = 5.1;
            cpu.Cell(2, 7).Value = 65;
            cpu.Cell(2, 8).Value = true;
            cpu.Cell(2, 9).Value = "DDR5";
            cpu.Cell(2, 10).Value = 5200;
        }

        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return output.ToArray();
    }

    private static MultipartFormDataContent CreateWorkbookContent(byte[] bytes)
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", "products.xlsx");
        return content;
    }

    [Fact]
    public async Task DownloadTemplate_WhenAnonymous_Returns401Unauthorized()
    {
        var client = await CreateClientForRoleAsync(null);

        var response = await client.GetAsync("/api/v1/product-imports/template");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DownloadTemplate_WhenCustomer_Returns403Forbidden()
    {
        var client = await CreateClientForRoleAsync(UserRole.Customer);

        var response = await client.GetAsync("/api/v1/product-imports/template");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DownloadTemplate_WhenStaffOrAdmin_Returns200WithReadableXlsx()
    {
        // Seed category to test category lookup in template
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Categories.Add(new Category("Vi xử lý Test", "cpu-test", ComponentType.Cpu));
            await db.SaveChangesAsync();
        }

        var client = await CreateClientForRoleAsync(UserRole.Staff);

        var response = await client.GetAsync("/api/v1/product-imports/template");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        response.Content.Headers.ContentDisposition?.FileName.Should().Be("riutek-product-import-v1.0.xlsx");

        var fileBytes = await response.Content.ReadAsByteArrayAsync();
        fileBytes.Should().NotBeEmpty();

        using var ms = new MemoryStream(fileBytes);
        using var workbook = new XLWorkbook(ms);
        workbook.Worksheets.Count.Should().Be(13);
        workbook.TryGetWorksheet("Products", out _).Should().BeTrue();
        workbook.TryGetWorksheet("_Meta", out _).Should().BeTrue();
    }

    [Fact]
    public async Task ValidateImport_WhenMissingOrWrongFile_Returns400BadRequest()
    {
        var client = await CreateClientForRoleAsync(UserRole.Admin);

        // 1. Missing file in multipart form
        using var emptyContent = new MultipartFormDataContent();
        var emptyResp = await client.PostAsync("/api/v1/product-imports/validate", emptyContent);
        emptyResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 2. Wrong extension (.csv instead of .xlsx)
        using var csvContent = new MultipartFormDataContent();
        var fakeCsv = new ByteArrayContent(new byte[100]);
        fakeCsv.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        csvContent.Add(fakeCsv, "file", "products.csv");
        var csvResp = await client.PostAsync("/api/v1/product-imports/validate", csvContent);
        csvResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ValidateImport_ValidMixedWorkbook_Returns200_WithIsValidTrue()
    {
        // Seed categories in PostgreSQL
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Categories.Add(new Category("Vi xử lý", "cpu", ComponentType.Cpu));
            db.Categories.Add(new Category("Card đồ họa", "vga", ComponentType.Gpu));
            await db.SaveChangesAsync();
        }

        // Generate template and build mixed content (CPU + GPU)
        using var scopeServices = _fixture.Factory.Services.CreateScope();
        var workbookService = scopeServices.ServiceProvider.GetRequiredService<IProductImportWorkbookService>();
        var template = await workbookService.GenerateTemplateAsync([
            new("cpu", "Vi xử lý", ComponentType.Cpu),
            new("vga", "Card đồ họa", ComponentType.Gpu)
        ]);

        using var ms = new MemoryStream(template.Content);
        using var wb = new XLWorkbook(ms);

        var pWs = wb.Worksheet("Products");
        pWs.Cell(2, 1).Value = "SKU-MIXED-CPU";
        pWs.Cell(2, 2).Value = "AMD Ryzen 9 7900X";
        pWs.Cell(2, 3).Value = "cpu";
        pWs.Cell(2, 4).Value = "Cpu";
        pWs.Cell(2, 5).Value = "AMD";
        pWs.Cell(2, 6).Value = 11000000;
        pWs.Cell(2, 7).Value = 12000000;
        pWs.Cell(2, 8).Value = 10;
        pWs.Cell(2, 10).Value = "https://media.riutek.com/7900x.png";

        pWs.Cell(3, 1).Value = "SKU-MIXED-GPU";
        pWs.Cell(3, 2).Value = "ASUS ROG RTX 4080";
        pWs.Cell(3, 3).Value = "vga";
        pWs.Cell(3, 4).Value = "Gpu";
        pWs.Cell(3, 5).Value = "ASUS";
        pWs.Cell(3, 6).Value = 31000000;
        pWs.Cell(3, 8).Value = 5;
        pWs.Cell(3, 10).Value = "https://media.riutek.com/4080.png";

        var cpuWs = wb.Worksheet("CPU");
        cpuWs.Cell(2, 1).Value = "SKU-MIXED-CPU";
        cpuWs.Cell(2, 2).Value = "AM5";
        cpuWs.Cell(2, 3).Value = 12;
        cpuWs.Cell(2, 4).Value = 24;
        cpuWs.Cell(2, 5).Value = 4.7;
        cpuWs.Cell(2, 6).Value = 5.6;
        cpuWs.Cell(2, 7).Value = 170;
        cpuWs.Cell(2, 8).Value = true;
        cpuWs.Cell(2, 9).Value = "DDR5";
        cpuWs.Cell(2, 10).Value = 5200;

        var gpuWs = wb.Worksheet("GPU");
        gpuWs.Cell(2, 1).Value = "SKU-MIXED-GPU";
        gpuWs.Cell(2, 2).Value = "AD103";
        gpuWs.Cell(2, 3).Value = 16;
        gpuWs.Cell(2, 4).Value = "GDDR6X";
        gpuWs.Cell(2, 5).Value = 304;
        gpuWs.Cell(2, 6).Value = 2.5;
        gpuWs.Cell(2, 7).Value = 320;
        gpuWs.Cell(2, 8).Value = 750;
        gpuWs.Cell(2, 9).Value = true;
        gpuWs.Cell(2, 10).Value = "1x 16-pin";

        using var saveMs = new MemoryStream();
        wb.SaveAs(saveMs);
        var xlsxBytes = saveMs.ToArray();

        var client = await CreateClientForRoleAsync(UserRole.Admin);

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(xlsxBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", "mixed-import.xlsx");

        // Act
        var response = await client.PostAsync("/api/v1/product-imports/validate", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var resultDto = await response.Content.ReadFromJsonAsync<ProductImportValidationResultDto>(JsonOptions);
        resultDto.Should().NotBeNull();
        resultDto!.IsValid.Should().BeTrue();
        resultDto.TotalRows.Should().Be(2);
        resultDto.ValidRows.Should().Be(2);
        resultDto.InvalidRows.Should().Be(0);
        resultDto.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateImport_SeedSkuAndCategoryConflicts_CatchesDbConflicts()
    {
        // Seed category and existing product in PostgreSQL
        Guid catId;
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var category = new Category("Vi xử lý", "cpu", ComponentType.Cpu);
            db.Categories.Add(category);
            await db.SaveChangesAsync();
            catId = category.Id;

            var existingProd = new Product(
                catId,
                "AMD Ryzen 7 5700X",
                "amd-ryzen-7-5700x",
                "SKU-CONFLICT-01",
                "AMD",
                4500000,
                10,
                "https://media.riutek.com/5700x.png",
                ComponentType.Cpu,
                new CpuSpecification { Socket = CpuSocket.AM4, CoreCount = 8, ThreadCount = 16, BaseClockGhz = 3.4, BoostClockGhz = 4.6, TdpWattage = 65, SupportedMemoryType = RamType.DDR4, MaxMemorySpeedMhz = 3200 });
            db.Products.Add(existingProd);
            await db.SaveChangesAsync();
        }

        // Create workbook with SKU conflict and missing category
        using var scopeServices = _fixture.Factory.Services.CreateScope();
        var workbookService = scopeServices.ServiceProvider.GetRequiredService<IProductImportWorkbookService>();
        var template = await workbookService.GenerateTemplateAsync([new("cpu", "Vi xử lý", ComponentType.Cpu)]);

        using var ms = new MemoryStream(template.Content);
        using var wb = new XLWorkbook(ms);

        var pWs = wb.Worksheet("Products");
        // Row 2: Conflicts with existing SKU in DB
        pWs.Cell(2, 1).Value = "sku-conflict-01"; // Case-insensitive collision
        pWs.Cell(2, 2).Value = "Some Different Name";
        pWs.Cell(2, 3).Value = "cpu";
        pWs.Cell(2, 4).Value = "Cpu";
        pWs.Cell(2, 5).Value = "AMD";
        pWs.Cell(2, 6).Value = 4000000;
        pWs.Cell(2, 8).Value = 10;
        pWs.Cell(2, 10).Value = "https://media.riutek.com/img.png";

        // Row 3: Category not found in DB
        pWs.Cell(3, 1).Value = "SKU-NEW-02";
        pWs.Cell(3, 2).Value = "AMD Ryzen 5 7600";
        pWs.Cell(3, 3).Value = "non-existent-category";
        pWs.Cell(3, 4).Value = "Cpu";
        pWs.Cell(3, 5).Value = "AMD";
        pWs.Cell(3, 6).Value = 5000000;
        pWs.Cell(3, 8).Value = 10;
        pWs.Cell(3, 10).Value = "https://media.riutek.com/img.png";

        var cpuWs = wb.Worksheet("CPU");
        cpuWs.Cell(2, 1).Value = "sku-conflict-01";
        cpuWs.Cell(2, 2).Value = "AM4"; cpuWs.Cell(2, 3).Value = 8; cpuWs.Cell(2, 4).Value = 16; cpuWs.Cell(2, 5).Value = 3.4; cpuWs.Cell(2, 6).Value = 4.6; cpuWs.Cell(2, 7).Value = 65; cpuWs.Cell(2, 8).Value = false; cpuWs.Cell(2, 9).Value = "DDR4"; cpuWs.Cell(2, 10).Value = 3200;

        cpuWs.Cell(3, 1).Value = "SKU-NEW-02";
        cpuWs.Cell(3, 2).Value = "AM5"; cpuWs.Cell(3, 3).Value = 6; cpuWs.Cell(3, 4).Value = 12; cpuWs.Cell(3, 5).Value = 3.8; cpuWs.Cell(3, 6).Value = 5.1; cpuWs.Cell(3, 7).Value = 65; cpuWs.Cell(3, 8).Value = true; cpuWs.Cell(3, 9).Value = "DDR5"; cpuWs.Cell(3, 10).Value = 5200;

        using var saveMs = new MemoryStream();
        wb.SaveAs(saveMs);
        var xlsxBytes = saveMs.ToArray();

        var client = await CreateClientForRoleAsync(UserRole.Admin);

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(xlsxBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", "conflict-import.xlsx");

        // Act
        var response = await client.PostAsync("/api/v1/product-imports/validate", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var resultDto = await response.Content.ReadFromJsonAsync<ProductImportValidationResultDto>(JsonOptions);
        resultDto.Should().NotBeNull();
        resultDto!.IsValid.Should().BeFalse();
        resultDto.Errors.Should().Contain(e => e.Code == "ProductImport.SkuConflict" && e.Row == 2);
        resultDto.Errors.Should().Contain(e => e.Code == "ProductImport.CategoryNotFound" && e.Row == 3);
    }

    [Fact]
    public async Task ValidateImport_DryRunDoesNotMutateDatabase()
    {
        // 1. Seed initial data
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var cat = new Category("Vi xử lý", "cpu", ComponentType.Cpu);
            db.Categories.Add(cat);
            db.Products.Add(new Product(
                cat.Id, "Initial Product", "initial-product", "SKU-INIT-1", "Brand", 1000, 5,
                "https://media.riutek.com/img.png", ComponentType.Cpu,
                new CpuSpecification { Socket = CpuSocket.AM4, CoreCount = 4, ThreadCount = 8, BaseClockGhz = 3.0, BoostClockGhz = 3.8, TdpWattage = 65, SupportedMemoryType = RamType.DDR4, MaxMemorySpeedMhz = 3200 }));
            await db.SaveChangesAsync();
        }

        int initialProductCount;
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            initialProductCount = await db.Products.CountAsync();
        }

        // 2. Perform dry-run validation with valid new product
        using var scopeServices = _fixture.Factory.Services.CreateScope();
        var workbookService = scopeServices.ServiceProvider.GetRequiredService<IProductImportWorkbookService>();
        var template = await workbookService.GenerateTemplateAsync([new("cpu", "Vi xử lý", ComponentType.Cpu)]);

        using var ms = new MemoryStream(template.Content);
        using var wb = new XLWorkbook(ms);

        var pWs = wb.Worksheet("Products");
        pWs.Cell(2, 1).Value = "SKU-DRYRUN-TEST";
        pWs.Cell(2, 2).Value = "Dry Run Test Product";
        pWs.Cell(2, 3).Value = "cpu";
        pWs.Cell(2, 4).Value = "Cpu";
        pWs.Cell(2, 5).Value = "Brand";
        pWs.Cell(2, 6).Value = 5000000;
        pWs.Cell(2, 8).Value = 10;
        pWs.Cell(2, 10).Value = "https://media.riutek.com/dryrun.png";

        var cpuWs = wb.Worksheet("CPU");
        cpuWs.Cell(2, 1).Value = "SKU-DRYRUN-TEST";
        cpuWs.Cell(2, 2).Value = "AM4"; cpuWs.Cell(2, 3).Value = 6; cpuWs.Cell(2, 4).Value = 12; cpuWs.Cell(2, 5).Value = 3.5; cpuWs.Cell(2, 6).Value = 4.4; cpuWs.Cell(2, 7).Value = 65; cpuWs.Cell(2, 8).Value = false; cpuWs.Cell(2, 9).Value = "DDR4"; cpuWs.Cell(2, 10).Value = 3200;

        using var saveMs = new MemoryStream();
        wb.SaveAs(saveMs);

        var client = await CreateClientForRoleAsync(UserRole.Admin);

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(saveMs.ToArray());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", "dry-run.xlsx");

        var response = await client.PostAsync("/api/v1/product-imports/validate", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Verify Database Product count is 100% UNCHANGED
        int finalProductCount;
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            finalProductCount = await db.Products.CountAsync();
        }

        finalProductCount.Should().Be(initialProductCount);
    }

    [Fact]
    public async Task Import_ValidWorkbook_CreatesWholeBatchAndReturns201()
    {
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Categories.Add(new Category("Vi xử lý", "cpu", ComponentType.Cpu));
            await db.SaveChangesAsync();
        }

        var bytes = await CreateCpuWorkbookAsync("CPU-IMPORT-7600", "AMD Ryzen 5 7600");
        var client = await CreateClientForRoleAsync(UserRole.Staff);
        using var content = CreateWorkbookContent(bytes);

        var response = await client.PostAsync("/api/v1/product-imports/import", content);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = await response.Content.ReadFromJsonAsync<ProductImportCommitResultDto>(JsonOptions);
        result.Should().NotBeNull();
        result!.IsCommitted.Should().BeTrue();
        result.CreatedCount.Should().Be(1);
        result.CreatedProducts.Should().ContainSingle(item =>
            item.Sku == "CPU-IMPORT-7600" && item.Slug == "amd-ryzen-5-7600");

        using var assertScope = _fixture.Factory.Services.CreateScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var product = await assertDb.Products.AsNoTracking().SingleAsync();
        product.Sku.Should().Be("CPU-IMPORT-7600");
        product.StockQuantity.Should().Be(10);
        product.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Import_InvalidWorkbook_IsAllOrNothingAndReturnsValidationReport()
    {
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Categories.Add(new Category("Vi xử lý", "cpu", ComponentType.Cpu));
            await db.SaveChangesAsync();
        }

        var bytes = await CreateCpuWorkbookAsync(
            "CPU-MISSING-SPEC",
            "CPU Missing Specification",
            includeSpecification: false);
        var client = await CreateClientForRoleAsync(UserRole.Admin);
        using var content = CreateWorkbookContent(bytes);

        var response = await client.PostAsync("/api/v1/product-imports/import", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ProductImportCommitResultDto>(JsonOptions);
        result.Should().NotBeNull();
        result!.IsCommitted.Should().BeFalse();
        result.CreatedCount.Should().Be(0);
        result.Validation.Errors.Should().Contain(error =>
            error.Code == "ProductImport.MissingSpecification" && error.Sku == "CPU-MISSING-SPEC");

        using var assertScope = _fixture.Factory.Services.CreateScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await assertDb.Products.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Import_ReplayingSameWorkbook_DoesNotCreateDuplicates()
    {
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Categories.Add(new Category("Vi xử lý", "cpu", ComponentType.Cpu));
            await db.SaveChangesAsync();
        }

        var bytes = await CreateCpuWorkbookAsync("CPU-REPLAY-01", "CPU Replay Safe");
        var client = await CreateClientForRoleAsync(UserRole.Admin);
        using var firstContent = CreateWorkbookContent(bytes);
        using var secondContent = CreateWorkbookContent(bytes);

        var firstResponse = await client.PostAsync("/api/v1/product-imports/import", firstContent);
        var secondResponse = await client.PostAsync("/api/v1/product-imports/import", secondContent);

        firstResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var replayResult = await secondResponse.Content.ReadFromJsonAsync<ProductImportCommitResultDto>(JsonOptions);
        replayResult.Should().NotBeNull();
        replayResult!.IsCommitted.Should().BeFalse();
        replayResult.CreatedCount.Should().Be(0);
        replayResult.Validation.Errors.Should().Contain(error =>
            error.Code == "ProductImport.SkuConflict" ||
            error.Code == "ProductImport.SlugConflict");

        using var assertScope = _fixture.Factory.Services.CreateScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await assertDb.Products.CountAsync()).Should().Be(1);
    }
}

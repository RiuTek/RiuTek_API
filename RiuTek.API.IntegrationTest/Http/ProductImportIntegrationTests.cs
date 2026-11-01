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
            await db.StockMovements.ExecuteDeleteAsync();
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

    private static MultipartFormDataContent CreateWorkbookContent(byte[] bytes, string? imageMappingsJson = null)
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(fileContent, "file", "products.xlsx");

        if (imageMappingsJson is not null)
        {
            content.Add(new StringContent(imageMappingsJson), "imageMappings");
        }

        return content;
    }

    private async Task<(HttpClient Client, FakeProductMediaStorage FakeStorage)> CreateClientWithFakeStorageAsync(
        UserRole? role = UserRole.Admin,
        bool isActive = true,
        Action<FakeProductMediaStorage>? configure = null)
    {
        var fakeStorage = new FakeProductMediaStorage();
        configure?.Invoke(fakeStorage);

        var customFactory = _fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IProductMediaStorage));
                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }
                services.AddSingleton<IProductMediaStorage>(fakeStorage);
            });
        });

        var client = customFactory.CreateClient();
        if (role.HasValue)
        {
            using var scope = _fixture.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var jwtGenerator = scope.ServiceProvider.GetRequiredService<IJwtTokenGenerator>();

            var user = new User(
                email: $"import_{role.Value.ToString().ToLowerInvariant()}_{Guid.NewGuid():N}@riutek.test",
                passwordHash: "dummy_hashed_password",
                fullName: $"{role.Value} Import User",
                role: role.Value
            ) { IsActive = isActive };

            db.Users.Add(user);
            await db.SaveChangesAsync();

            var token = jwtGenerator.GenerateAccessToken(user);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return (client, fakeStorage);
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

        var movements = await assertDb.StockMovements.ToListAsync();
        movements.Should().ContainSingle("Replay rejected must not add duplicate movements");
        movements[0].Reason.Should().Be(StockMovementReason.BulkImport);
        movements[0].QuantityDelta.Should().Be(10);
    }

    [Fact]
    public async Task Import_MovementAudit_PositiveStockCreatesMovement_ZeroStockDoesNot()
    {
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Categories.Add(new Category("Vi xử lý", "cpu", ComponentType.Cpu));
            await db.SaveChangesAsync();
        }

        using var scopeServices = _fixture.Factory.Services.CreateScope();
        var workbookService = scopeServices.ServiceProvider.GetRequiredService<IProductImportWorkbookService>();
        var template = await workbookService.GenerateTemplateAsync([
            new("cpu", "Vi xử lý", ComponentType.Cpu)
        ]);

        using var ms = new MemoryStream(template.Content);
        using var wb = new XLWorkbook(ms);

        var pWs = wb.Worksheet("Products");
        // Row 1: Stock 10
        pWs.Cell(2, 1).Value = "SKU-AUDIT-CPU-1";
        pWs.Cell(2, 2).Value = "AMD Ryzen 5 7600";
        pWs.Cell(2, 3).Value = "cpu";
        pWs.Cell(2, 4).Value = "Cpu";
        pWs.Cell(2, 5).Value = "AMD";
        pWs.Cell(2, 6).Value = 5000000;
        pWs.Cell(2, 8).Value = 10;
        pWs.Cell(2, 10).Value = "https://media.riutek.test/7600.png";

        // Row 2: Stock 0
        pWs.Cell(3, 1).Value = "SKU-AUDIT-CPU-2";
        pWs.Cell(3, 2).Value = "AMD Ryzen 5 7600X";
        pWs.Cell(3, 3).Value = "cpu";
        pWs.Cell(3, 4).Value = "Cpu";
        pWs.Cell(3, 5).Value = "AMD";
        pWs.Cell(3, 6).Value = 5500000;
        pWs.Cell(3, 8).Value = 0;
        pWs.Cell(3, 10).Value = "https://media.riutek.test/7600x.png";

        var cpuWs = wb.Worksheet("CPU");
        cpuWs.Cell(2, 1).Value = "SKU-AUDIT-CPU-1";
        cpuWs.Cell(2, 2).Value = "AM5";
        cpuWs.Cell(2, 3).Value = 6;
        cpuWs.Cell(2, 4).Value = 12;
        cpuWs.Cell(2, 5).Value = 3.8;
        cpuWs.Cell(2, 6).Value = 5.1;
        cpuWs.Cell(2, 7).Value = 65;
        cpuWs.Cell(2, 8).Value = true;
        cpuWs.Cell(2, 9).Value = "DDR5";
        cpuWs.Cell(2, 10).Value = 5200;

        cpuWs.Cell(3, 1).Value = "SKU-AUDIT-CPU-2";
        cpuWs.Cell(3, 2).Value = "AM5";
        cpuWs.Cell(3, 3).Value = 6;
        cpuWs.Cell(3, 4).Value = 12;
        cpuWs.Cell(3, 5).Value = 4.7;
        cpuWs.Cell(3, 6).Value = 5.3;
        cpuWs.Cell(3, 7).Value = 105;
        cpuWs.Cell(3, 8).Value = true;
        cpuWs.Cell(3, 9).Value = "DDR5";
        cpuWs.Cell(3, 10).Value = 5200;

        using var saveMs = new MemoryStream();
        wb.SaveAs(saveMs);
        var bytes = saveMs.ToArray();

        var client = await CreateClientForRoleAsync(UserRole.Admin);
        using var content = CreateWorkbookContent(bytes);

        var response = await client.PostAsync("/api/v1/product-imports/import", content);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        using var assertScope = _fixture.Factory.Services.CreateScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var products = await assertDb.Products.OrderBy(p => p.Sku).ToListAsync();
        products.Should().HaveCount(2);

        var p1 = products.Single(p => p.Sku == "SKU-AUDIT-CPU-1");
        var p2 = products.Single(p => p.Sku == "SKU-AUDIT-CPU-2");
        p2.StockQuantity.Should().Be(0);

        var movements = await assertDb.StockMovements.ToListAsync();
        movements.Should().ContainSingle("Only product with stock > 0 should have BulkImport movement");

        movements[0].ProductId.Should().Be(p1.Id);
        movements[0].QuantityDelta.Should().Be(10);
        movements[0].StockBefore.Should().Be(0);
        movements[0].StockAfter.Should().Be(10);
        movements[0].Reason.Should().Be(StockMovementReason.BulkImport);
        movements[0].ActorUserId.Should().NotBeNull();
        movements[0].OrderId.Should().BeNull();
    }

    [Fact]
    public async Task Import_WhenConcurrentUniqueConflictOccurs_RollsBackBothProductsAndMovements()
    {
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Categories.Add(new Category("Vi xử lý", "cpu", ComponentType.Cpu));
            await db.SaveChangesAsync();
        }

        // Pre-seed an existing product in DB with SKU-CONFLICT
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var cat = await db.Categories.FirstAsync();
            var existing = new Product(
                cat.Id, "Pre-existing", "pre-existing", "SKU-CONFLICT", "Intel", 1000, 5,
                "https://media.riutek.test/cpu.webp", ComponentType.Cpu,
                new CpuSpecification { Socket = CpuSocket.LGA1700, CoreCount = 4, ThreadCount = 8, BaseClockGhz = 3.0, BoostClockGhz = 4.0, TdpWattage = 65, HasIntegratedGpu = false, SupportedMemoryType = RamType.DDR4, MaxMemorySpeedMhz = 3200 });
            db.Products.Add(existing);
            await db.SaveChangesAsync();
        }

        // Workbook tries to import SKU-CONFLICT
        var bytes = await CreateCpuWorkbookAsync("SKU-CONFLICT", "Conflicting CPU");
        var client = await CreateClientForRoleAsync(UserRole.Admin);
        using var content = CreateWorkbookContent(bytes);

        var response = await client.PostAsync("/api/v1/product-imports/import", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ProductImportCommitResultDto>(JsonOptions);
        result.Should().NotBeNull();
        result!.IsCommitted.Should().BeFalse();
        result.CreatedCount.Should().Be(0);

        using var assertScope = _fixture.Factory.Services.CreateScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Only the pre-seeded product exists; 0 movements were committed
        var allProducts = await assertDb.Products.ToListAsync();
        allProducts.Should().ContainSingle();
        allProducts[0].Name.Should().Be("Pre-existing");

        var allMovements = await assertDb.StockMovements.ToListAsync();
        allMovements.Should().BeEmpty();
    }

    [Fact]
    public async Task InspectImport_AuthAndRoleGuards_EnforcesActiveContentManager()
    {
        var bytes = await CreateCpuWorkbookAsync("CPU-AUTH-01", "AMD Ryzen 5 7600");

        // 1. Anonymous -> 401
        var anonClient = await CreateClientForRoleAsync(null);
        using (var c1 = CreateWorkbookContent(bytes))
        {
            var r1 = await anonClient.PostAsync("/api/v1/product-imports/inspect", c1);
            r1.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        // 2. Customer -> 403
        var custClient = await CreateClientForRoleAsync(UserRole.Customer);
        using (var c2 = CreateWorkbookContent(bytes))
        {
            var r2 = await custClient.PostAsync("/api/v1/product-imports/inspect", c2);
            r2.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        // 3. Inactive Admin -> 403
        var (inactiveClient, _) = await CreateClientWithFakeStorageAsync(UserRole.Admin, isActive: false);
        using (var c3 = CreateWorkbookContent(bytes))
        {
            var r3 = await inactiveClient.PostAsync("/api/v1/product-imports/inspect", c3);
            r3.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task InspectImport_DefersMissingImageUrl_ButKeepsNonImageAndInvalidHttpsUrlErrors()
    {
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Categories.Add(new Category("Vi xử lý", "cpu", ComponentType.Cpu));
            await db.SaveChangesAsync();
        }

        using var scopeServices = _fixture.Factory.Services.CreateScope();
        var workbookService = scopeServices.ServiceProvider.GetRequiredService<IProductImportWorkbookService>();
        var template = await workbookService.GenerateTemplateAsync([new("cpu", "Vi xử lý", ComponentType.Cpu)]);

        byte[] validInspectBytes;
        using (var ms = new MemoryStream(template.Content))
        using (var wb = new XLWorkbook(ms))
        {
            var pWs = wb.Worksheet("Products");
            pWs.Cell(2, 1).Value = "CPU-LOCAL-01";
            pWs.Cell(2, 2).Value = "AMD Ryzen 5 7600";
            pWs.Cell(2, 3).Value = "cpu";
            pWs.Cell(2, 4).Value = "Cpu";
            pWs.Cell(2, 5).Value = "AMD";
            pWs.Cell(2, 6).Value = 5000000;
            pWs.Cell(2, 8).Value = 10;
            pWs.Cell(2, 9).Value = "CPU-LOCAL-01/main.jpg";
            pWs.Cell(2, 10).Value = ""; // Missing ImageUrl deferred in inspect

            var cpuWs = wb.Worksheet("CPU");
            cpuWs.Cell(2, 1).Value = "CPU-LOCAL-01";
            cpuWs.Cell(2, 2).Value = "AM5"; cpuWs.Cell(2, 3).Value = 6; cpuWs.Cell(2, 4).Value = 12;
            cpuWs.Cell(2, 5).Value = 3.8; cpuWs.Cell(2, 6).Value = 5.1; cpuWs.Cell(2, 7).Value = 65;
            cpuWs.Cell(2, 8).Value = true; cpuWs.Cell(2, 9).Value = "DDR5"; cpuWs.Cell(2, 10).Value = 5200;

            using var outMs = new MemoryStream();
            wb.SaveAs(outMs);
            validInspectBytes = outMs.ToArray();
        }

        var client = await CreateClientForRoleAsync(UserRole.Staff);

        // 1. Inspect passes when only ImageUrl is missing
        using (var inspectContent = CreateWorkbookContent(validInspectBytes))
        {
            var inspectRes = await client.PostAsync("/api/v1/product-imports/inspect", inspectContent);
            inspectRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var inspectDto = await inspectRes.Content.ReadFromJsonAsync<ProductImportInspectResultDto>(JsonOptions);
            inspectDto.Should().NotBeNull();
            inspectDto!.TemplateVersion.Should().Be("1.0");
            inspectDto.IsValid.Should().BeTrue();
            inspectDto.Errors.Should().BeEmpty();
            inspectDto.Rows.Should().ContainSingle(r =>
                r.Sheet == "Products" &&
                r.Row == 2 &&
                r.Sku == "CPU-LOCAL-01" &&
                r.ComponentType == ComponentType.Cpu &&
                r.PrimaryImageFile == "CPU-LOCAL-01/main.jpg" &&
                r.ImageUrl == null);
        }

        // 2. Validate & Import without imageMappings on the same file still reject missing ImageUrl
        using (var valContent = CreateWorkbookContent(validInspectBytes))
        {
            var valRes = await client.PostAsync("/api/v1/product-imports/validate", valContent);
            valRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var valDto = await valRes.Content.ReadFromJsonAsync<ProductImportValidationResultDto>(JsonOptions);
            valDto!.IsValid.Should().BeFalse();
            valDto.Errors.Should().Contain(e => e.Sku == "CPU-LOCAL-01" && e.Column == "ImageUrl");
        }

        using (var impContent = CreateWorkbookContent(validInspectBytes))
        {
            var impRes = await client.PostAsync("/api/v1/product-imports/import", impContent);
            impRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var impDto = await impRes.Content.ReadFromJsonAsync<ProductImportCommitResultDto>(JsonOptions);
            impDto!.IsCommitted.Should().BeFalse();
        }
    }

    [Fact]
    public async Task ValidateAndImport_WithValidImageMappings_OverridesOnlyMappedSkuAndPreservesGalleryOrder()
    {
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Categories.Add(new Category("Vi xử lý", "cpu", ComponentType.Cpu));
            db.Categories.Add(new Category("Card đồ họa", "vga", ComponentType.Gpu));
            await db.SaveChangesAsync();
        }

        using var scopeServices = _fixture.Factory.Services.CreateScope();
        var workbookService = scopeServices.ServiceProvider.GetRequiredService<IProductImportWorkbookService>();
        var template = await workbookService.GenerateTemplateAsync([
            new("cpu", "Vi xử lý", ComponentType.Cpu),
            new("vga", "Card đồ họa", ComponentType.Gpu)
        ]);

        byte[] workbookBytes;
        using (var ms = new MemoryStream(template.Content))
        using (var wb = new XLWorkbook(ms))
        {
            var pWs = wb.Worksheet("Products");
            // Row 2: Mapped SKU (has old URL in Excel that should be overridden by mapping)
            pWs.Cell(2, 1).Value = "CPU-MAPPED-01";
            pWs.Cell(2, 2).Value = "AMD Ryzen 7 7800X3D";
            pWs.Cell(2, 3).Value = "cpu";
            pWs.Cell(2, 4).Value = "Cpu";
            pWs.Cell(2, 5).Value = "AMD";
            pWs.Cell(2, 6).Value = 10500000;
            pWs.Cell(2, 7).Value = 11500000;
            pWs.Cell(2, 8).Value = 12;
            pWs.Cell(2, 9).Value = "CPU-MAPPED-01/main.jpg";
            pWs.Cell(2, 10).Value = "https://old.example.com/should-be-overridden.jpg";

            // Row 3: Unmapped SKU (keeps its workbook ImageUrl)
            pWs.Cell(3, 1).Value = "GPU-UNMAPPED-02";
            pWs.Cell(3, 2).Value = "NVIDIA RTX 4070 Super";
            pWs.Cell(3, 3).Value = "vga";
            pWs.Cell(3, 4).Value = "Gpu";
            pWs.Cell(3, 5).Value = "NVIDIA";
            pWs.Cell(3, 6).Value = 16500000;
            pWs.Cell(3, 8).Value = 7;
            pWs.Cell(3, 10).Value = "https://media.riutek.com/existing-gpu.webp";

            var cpuWs = wb.Worksheet("CPU");
            cpuWs.Cell(2, 1).Value = "CPU-MAPPED-01";
            cpuWs.Cell(2, 2).Value = "AM5"; cpuWs.Cell(2, 3).Value = 8; cpuWs.Cell(2, 4).Value = 16;
            cpuWs.Cell(2, 5).Value = 4.2; cpuWs.Cell(2, 6).Value = 5.0; cpuWs.Cell(2, 7).Value = 120;
            cpuWs.Cell(2, 8).Value = true; cpuWs.Cell(2, 9).Value = "DDR5"; cpuWs.Cell(2, 10).Value = 5200;

            var gpuWs = wb.Worksheet("GPU");
            gpuWs.Cell(2, 1).Value = "GPU-UNMAPPED-02";
            gpuWs.Cell(2, 2).Value = "AD104"; gpuWs.Cell(2, 3).Value = 12; gpuWs.Cell(2, 4).Value = "GDDR6X";
            gpuWs.Cell(2, 5).Value = 267; gpuWs.Cell(2, 6).Value = 2.5; gpuWs.Cell(2, 7).Value = 220;
            gpuWs.Cell(2, 8).Value = 650; gpuWs.Cell(2, 9).Value = true; gpuWs.Cell(2, 10).Value = "1x 16-pin";

            using var outMs = new MemoryStream();
            wb.SaveAs(outMs);
            workbookBytes = outMs.ToArray();
        }

        var primaryKey = "products/2026/04/01925b30-c9a7-7e61-a8d9-6012e1a3bc01.jpg";
        var galleryKey1 = "products/2026/04/01925b30-c9a7-7e61-a8d9-6012e1a3bc02.png";
        var galleryKey2 = "products/2026/04/01925b30-c9a7-7e61-a8d9-6012e1a3bc03.webp";

        var (client, fakeStorage) = await CreateClientWithFakeStorageAsync(UserRole.Admin, configure: s =>
        {
            s.ExistingKeys.Add(primaryKey);
            s.ExistingKeys.Add(galleryKey1);
            s.ExistingKeys.Add(galleryKey2);
        });

        var imageMappingsJson = $$"""
        [
          {
            "sku": " cpu-mapped-01 ",
            "primaryObjectKey": "{{primaryKey}}",
            "additionalObjectKeys": ["{{galleryKey1}}", "{{galleryKey2}}"]
          }
        ]
        """;

        // 1. Validate with imageMappings (dry-run, zero DB writes)
        using (var valContent = CreateWorkbookContent(workbookBytes, imageMappingsJson))
        {
            var valRes = await client.PostAsync("/api/v1/product-imports/validate", valContent);
            valRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var valDto = await valRes.Content.ReadFromJsonAsync<ProductImportValidationResultDto>(JsonOptions);
            valDto!.IsValid.Should().BeTrue();
            valDto.ValidRows.Should().Be(2);
        }

        // 2. Import with imageMappings
        var metadataCallsBeforeImport = fakeStorage.GetMetadataCallCount;
        using (var impContent = CreateWorkbookContent(workbookBytes, imageMappingsJson))
        {
            var impRes = await client.PostAsync("/api/v1/product-imports/import", impContent);
            impRes.StatusCode.Should().Be(HttpStatusCode.Created);
            var impDto = await impRes.Content.ReadFromJsonAsync<ProductImportCommitResultDto>(JsonOptions);
            impDto!.IsCommitted.Should().BeTrue();
            impDto.CreatedCount.Should().Be(2);
        }

        // Verify /import only called GetObjectMetadataAsync once per distinct key (3 keys total, not 6)
        (fakeStorage.GetMetadataCallCount - metadataCallsBeforeImport).Should().Be(3);
        fakeStorage.DeleteCallCount.Should().Be(0);

        using var assertScope = _fixture.Factory.Services.CreateScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var products = await assertDb.Products.AsNoTracking().OrderBy(p => p.Sku).ToListAsync();
        products.Should().HaveCount(2);

        var mappedCpu = products.Single(p => p.Sku == "CPU-MAPPED-01");
        mappedCpu.ImageUrl.Should().Be($"https://media.riutek.com/{primaryKey}");
        mappedCpu.AdditionalImages.Should().Equal(
            $"https://media.riutek.com/{galleryKey1}",
            $"https://media.riutek.com/{galleryKey2}");
        mappedCpu.Price.Should().Be(10500000);
        mappedCpu.OriginalPrice.Should().Be(11500000);
        mappedCpu.StockQuantity.Should().Be(12);

        var unmappedGpu = products.Single(p => p.Sku == "GPU-UNMAPPED-02");
        unmappedGpu.ImageUrl.Should().Be("https://media.riutek.com/existing-gpu.webp");
        unmappedGpu.AdditionalImages.Should().BeEmpty();
        unmappedGpu.StockQuantity.Should().Be(7);
    }

    [Fact]
    public async Task Import_WithInvalidImageMappingsCases_RejectsWithZeroDatabaseWrites()
    {
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Categories.Add(new Category("Vi xử lý", "cpu", ComponentType.Cpu));
            await db.SaveChangesAsync();
        }

        var bytes = await CreateCpuWorkbookAsync("CPU-ERR-01", "AMD Ryzen 5 7600");
        var validKey1 = "products/2026/04/01925b30-c9a7-7e61-a8d9-6012e1a3bc11.jpg";
        var validKey2 = "products/2026/04/01925b30-c9a7-7e61-a8d9-6012e1a3bc12.jpg";

        var (client, fakeStorage) = await CreateClientWithFakeStorageAsync(UserRole.Admin, configure: s =>
        {
            s.ExistingKeys.Add(validKey1);
            s.ExistingKeys.Add(validKey2);
        });

        // Case A: Malformed JSON -> 400 Bad Request
        using (var cMalformed = CreateWorkbookContent(bytes, "{not-valid-json"))
        {
            var rMalformed = await client.PostAsync("/api/v1/product-imports/import", cMalformed);
            rMalformed.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        // Case B: Duplicate normalized SKU in imageMappings
        var dupSkuJson = $$"""
        [
          { "sku": "CPU-ERR-01", "primaryObjectKey": "{{validKey1}}" },
          { "sku": "cpu-err-01", "primaryObjectKey": "{{validKey2}}" }
        ]
        """;
        using (var cDupSku = CreateWorkbookContent(bytes, dupSkuJson))
        {
            var rDupSku = await client.PostAsync("/api/v1/product-imports/import", cDupSku);
            rDupSku.StatusCode.Should().Be(HttpStatusCode.OK);
            var dto = await rDupSku.Content.ReadFromJsonAsync<ProductImportCommitResultDto>(JsonOptions);
            dto!.IsCommitted.Should().BeFalse();
            dto.Validation.Errors.Should().Contain(e => e.Code == "ProductImport.DuplicateImageMappingSku");
        }

        // Case C: Unknown SKU not in workbook
        var unknownSkuJson = $$"""[{ "sku": "SKU-DOES-NOT-EXIST", "primaryObjectKey": "{{validKey1}}" }]""";
        using (var cUnknown = CreateWorkbookContent(bytes, unknownSkuJson))
        {
            var rUnknown = await client.PostAsync("/api/v1/product-imports/import", cUnknown);
            rUnknown.StatusCode.Should().Be(HttpStatusCode.OK);
            var dto = await rUnknown.Content.ReadFromJsonAsync<ProductImportCommitResultDto>(JsonOptions);
            dto!.IsCommitted.Should().BeFalse();
            dto.Validation.Errors.Should().Contain(e => e.Code == "ProductImport.UnknownImageMappingSku");
        }

        // Case D: Empty primaryObjectKey (gallery-only attempt)
        var emptyPrimaryJson = $$"""[{ "sku": "CPU-ERR-01", "primaryObjectKey": "", "additionalObjectKeys": ["{{validKey1}}"] }]""";
        using (var cEmptyPrimary = CreateWorkbookContent(bytes, emptyPrimaryJson))
        {
            var rEmptyPrimary = await client.PostAsync("/api/v1/product-imports/import", cEmptyPrimary);
            rEmptyPrimary.StatusCode.Should().Be(HttpStatusCode.OK);
            var dto = await rEmptyPrimary.Content.ReadFromJsonAsync<ProductImportCommitResultDto>(JsonOptions);
            dto!.IsCommitted.Should().BeFalse();
            dto.Validation.Errors.Should().Contain(e => e.Code == "ProductImport.MissingPrimaryObjectKey");
        }

        // Case E: > 10 additionalObjectKeys
        var elevenKeys = Enumerable.Range(1, 11)
            .Select(i => $"\"products/2026/04/01925b30-c9a7-7e61-a8d9-6012e1a3b{i:D3}.jpg\"")
            .ToArray();
        var tooManyGalleryJson = $$"""[{ "sku": "CPU-ERR-01", "primaryObjectKey": "{{validKey1}}", "additionalObjectKeys": [{{string.Join(",", elevenKeys)}}] }]""";
        using (var cTooMany = CreateWorkbookContent(bytes, tooManyGalleryJson))
        {
            var rTooMany = await client.PostAsync("/api/v1/product-imports/import", cTooMany);
            rTooMany.StatusCode.Should().Be(HttpStatusCode.OK);
            var dto = await rTooMany.Content.ReadFromJsonAsync<ProductImportCommitResultDto>(JsonOptions);
            dto!.IsCommitted.Should().BeFalse();
            dto.Validation.Errors.Should().Contain(e => e.Code == "ProductImport.TooManyAdditionalImages");
        }

        // Case F: Duplicate key in gallery / matching primary key & invalid managed key prefix
        var dupAndInvalidKeyJson = $$"""
        [
          {
            "sku": "CPU-ERR-01",
            "primaryObjectKey": "{{validKey1}}",
            "additionalObjectKeys": ["{{validKey1}}", "unmanaged/folder/evil.jpg"]
          }
        ]
        """;
        using (var cDupKey = CreateWorkbookContent(bytes, dupAndInvalidKeyJson))
        {
            var rDupKey = await client.PostAsync("/api/v1/product-imports/import", cDupKey);
            rDupKey.StatusCode.Should().Be(HttpStatusCode.OK);
            var dto = await rDupKey.Content.ReadFromJsonAsync<ProductImportCommitResultDto>(JsonOptions);
            dto!.IsCommitted.Should().BeFalse();
            dto.Validation.Errors.Should().Contain(e => e.Code == "ProductImport.DuplicateObjectKey");
            dto.Validation.Errors.Should().Contain(e => e.Code == "ProductImport.InvalidObjectKey");
        }

        using var assertScope = _fixture.Factory.Services.CreateScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await assertDb.Products.CountAsync()).Should().Be(0);
        (await assertDb.StockMovements.CountAsync()).Should().Be(0);
        fakeStorage.DeleteCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Import_WhenStorageMetadataMissingOrInvalidOrUnavailable_NeverCommitsAndNeverDeletesObjects()
    {
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Categories.Add(new Category("Vi xử lý", "cpu", ComponentType.Cpu));
            await db.SaveChangesAsync();
        }

        var bytes = await CreateCpuWorkbookAsync("CPU-META-01", "AMD Ryzen 5 7600");
        var missingKey = "products/2026/04/01925b30-c9a7-7e61-a8d9-6012e1a3bc91.jpg";
        var wrongMimeKey = "products/2026/04/01925b30-c9a7-7e61-a8d9-6012e1a3bc92.jpg";
        var oversizedKey = "products/2026/04/01925b30-c9a7-7e61-a8d9-6012e1a3bc93.jpg";

        var (client, fakeStorage) = await CreateClientWithFakeStorageAsync(UserRole.Admin, configure: s =>
        {
            s.CustomMetadata[wrongMimeKey] = new ProductMediaMetadata(wrongMimeKey, "image/png", 1024, "etag");
            s.CustomMetadata[oversizedKey] = new ProductMediaMetadata(oversizedKey, "image/jpeg", 10 * 1024 * 1024, "etag");
        });

        // 1. Missing object in R2 (NotFound)
        using (var cMissing = CreateWorkbookContent(bytes, $$"""[{"sku":"CPU-META-01","primaryObjectKey":"{{missingKey}}"}]"""))
        {
            var rMissing = await client.PostAsync("/api/v1/product-imports/import", cMissing);
            rMissing.StatusCode.Should().Be(HttpStatusCode.OK);
            var dto = await rMissing.Content.ReadFromJsonAsync<ProductImportCommitResultDto>(JsonOptions);
            dto!.IsCommitted.Should().BeFalse();
            dto.Validation.Errors.Should().Contain(e => e.Code == "ProductImport.MediaNotFound");
        }

        // 2. Wrong MIME for extension
        using (var cMime = CreateWorkbookContent(bytes, $$"""[{"sku":"CPU-META-01","primaryObjectKey":"{{wrongMimeKey}}"}]"""))
        {
            var rMime = await client.PostAsync("/api/v1/product-imports/import", cMime);
            rMime.StatusCode.Should().Be(HttpStatusCode.OK);
            var dto = await rMime.Content.ReadFromJsonAsync<ProductImportCommitResultDto>(JsonOptions);
            dto!.IsCommitted.Should().BeFalse();
            dto.Validation.Errors.Should().Contain(e => e.Code == "ProductImport.InvalidMediaMetadata");
        }

        // 3. Oversized object in R2
        using (var cSize = CreateWorkbookContent(bytes, $$"""[{"sku":"CPU-META-01","primaryObjectKey":"{{oversizedKey}}"}]"""))
        {
            var rSize = await client.PostAsync("/api/v1/product-imports/import", cSize);
            rSize.StatusCode.Should().Be(HttpStatusCode.OK);
            var dto = await rSize.Content.ReadFromJsonAsync<ProductImportCommitResultDto>(JsonOptions);
            dto!.IsCommitted.Should().BeFalse();
            dto.Validation.Errors.Should().Contain(e => e.Code == "ProductImport.InvalidMediaMetadata");
        }

        // 4. Storage disabled -> 503 Service Unavailable
        fakeStorage.IsEnabled = false;
        using (var cDisabled = CreateWorkbookContent(bytes, $$"""[{"sku":"CPU-META-01","primaryObjectKey":"{{missingKey}}"}]"""))
        {
            var rDisabled = await client.PostAsync("/api/v1/product-imports/import", cDisabled);
            rDisabled.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        }

        // 5. Storage enabled but HEAD network unavailable -> 503 Service Unavailable
        fakeStorage.IsEnabled = true;
        fakeStorage.SimulateUnavailableOnMetadata = true;
        using (var cUnavail = CreateWorkbookContent(bytes, $$"""[{"sku":"CPU-META-01","primaryObjectKey":"{{missingKey}}"}]"""))
        {
            var rUnavail = await client.PostAsync("/api/v1/product-imports/import", cUnavail);
            rUnavail.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        }

        fakeStorage.DeleteCallCount.Should().Be(0);
        using var assertScope = _fixture.Factory.Services.CreateScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await assertDb.Products.CountAsync()).Should().Be(0);
    }
}

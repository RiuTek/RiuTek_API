using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RiuTek.API.IntegrationTest.Infrastructure;
using RiuTek.Application.Common.Models;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Products.Queries;
using RiuTek.Core.Entities;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;
using RiuTek.Infrastructure.Data;
using Xunit;

namespace RiuTek.API.IntegrationTest.Http;

[Collection(IntegrationTestCollection.Name)]
public class ProductSpecificationFilterIntegrationTests : CatalogIntegrationTestBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ProductSpecificationFilterIntegrationTests(PostgreSqlContainerFixture fixture)
        : base(fixture)
    {
    }

    private record SeededSpecsCatalog(
        Category CpuCat,
        Category MbCat,
        Category RamCat,
        Category StorageCat,
        Category PsuCat,
        Category CaseCat,
        Category CoolerCat,
        Category GpuCat,
        Product Cpu1,
        Product Cpu2,
        Product Mb1,
        Product Mb2,
        Product Ram1,
        Product Ram2,
        Product Storage1,
        Product Storage2,
        Product Psu1,
        Product Psu2,
        Product Case1,
        Product Case2,
        Product Cooler1,
        Product Cooler2,
        Product Gpu1,
        Product Gpu2
    );

    private async Task<SeededSpecsCatalog> SeedSpecificationCatalogAsync()
    {
        using var scope = Fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var cpuCat = new Category("CPUs", "cpus", ComponentType.Cpu);
        var mbCat = new Category("Motherboards", "motherboards", ComponentType.Motherboard);
        var ramCat = new Category("RAMs", "rams", ComponentType.Ram);
        var storageCat = new Category("Storages", "storages", ComponentType.Storage);
        var psuCat = new Category("PSUs", "psus", ComponentType.Psu);
        var caseCat = new Category("Cases", "cases", ComponentType.Case);
        var coolerCat = new Category("Coolers", "coolers", ComponentType.Cooler);
        var gpuCat = new Category("GPUs", "gpus", ComponentType.Gpu);

        db.Categories.AddRange(cpuCat, mbCat, ramCat, storageCat, psuCat, caseCat, coolerCat, gpuCat);
        await db.SaveChangesAsync();

        // 1. CPUs
        var cpu1 = new Product(cpuCat.Id, "AMD Ryzen 5 7600", "amd-ryzen-5-7600", "SKU-CPU-7600", "AMD", 5000000m, 10, "https://example.com/cpu1.jpg", ComponentType.Cpu,
            new CpuSpecification
            {
                Socket = CpuSocket.AM5,
                CoreCount = 6,
                ThreadCount = 12,
                BaseClockGhz = 3.8,
                BoostClockGhz = 5.1,
                TdpWattage = 65,
                HasIntegratedGpu = true,
                SupportedMemoryType = RamType.DDR5,
                MaxMemorySpeedMhz = 5200
            });

        var cpu2 = new Product(cpuCat.Id, "Intel Core i5-12400F", "intel-core-i5-12400f", "SKU-CPU-12400F", "Intel", 3500000m, 5, "https://example.com/cpu2.jpg", ComponentType.Cpu,
            new CpuSpecification
            {
                Socket = CpuSocket.LGA1700,
                CoreCount = 6,
                ThreadCount = 12,
                BaseClockGhz = 2.5,
                BoostClockGhz = 4.4,
                TdpWattage = 65,
                HasIntegratedGpu = false,
                SupportedMemoryType = RamType.DDR4,
                MaxMemorySpeedMhz = 3200
            });

        // 2. Motherboards
        var mb1 = new Product(mbCat.Id, "ASUS ROG STRIX B650E-F", "asus-rog-strix-b650e-f", "SKU-MB-B650E", "ASUS", 7000000m, 8, "https://example.com/mb1.jpg", ComponentType.Motherboard,
            new MotherboardSpecification
            {
                Socket = CpuSocket.AM5,
                Chipset = "B650E",
                FormFactor = MotherboardFormFactor.ATX,
                MemoryType = RamType.DDR5,
                HasWifi = true
            });

        var mb2 = new Product(mbCat.Id, "MSI PRO B760M-P", "msi-pro-b760m-p", "SKU-MB-B760M", "MSI", 3000000m, 12, "https://example.com/mb2.jpg", ComponentType.Motherboard,
            new MotherboardSpecification
            {
                Socket = CpuSocket.LGA1700,
                Chipset = "B760",
                FormFactor = MotherboardFormFactor.MicroATX,
                MemoryType = RamType.DDR4,
                HasWifi = false
            });

        // 3. RAM
        var ram1 = new Product(ramCat.Id, "Corsair Vengeance RGB DDR5 32GB", "corsair-vengeance-rgb-ddr5-32gb", "SKU-RAM-DDR5-RGB", "Corsair", 3200000m, 15, "https://example.com/ram1.jpg", ComponentType.Ram,
            new RamSpecification
            {
                MemoryType = RamType.DDR5,
                FormFactor = RamFormFactor.DIMM,
                CapacityGb = 32,
                SpeedMhz = 6000,
                HasRgb = true
            });

        var ram2 = new Product(ramCat.Id, "Kingston Fury Beast DDR4 16GB", "kingston-fury-beast-ddr4-16gb", "SKU-RAM-DDR4-NONRGB", "Kingston", 1200000m, 20, "https://example.com/ram2.jpg", ComponentType.Ram,
            new RamSpecification
            {
                MemoryType = RamType.DDR4,
                FormFactor = RamFormFactor.DIMM,
                CapacityGb = 16,
                SpeedMhz = 3200,
                HasRgb = false
            });

        // 4. Storage
        var storage1 = new Product(storageCat.Id, "Samsung 990 PRO 1TB", "samsung-990-pro-1tb", "SKU-SSD-990PRO-1TB", "Samsung", 2800000m, 25, "https://example.com/ssd1.jpg", ComponentType.Storage,
            new StorageSpecification
            {
                StorageType = StorageType.NVMe_M2,
                Interface = StorageInterface.PCIe_4_0,
                CapacityGb = 1000,
                ReadSpeedMBs = 7450,
                WriteSpeedMBs = 6900
            });

        var storage2 = new Product(storageCat.Id, "Crucial BX500 500GB", "crucial-bx500-500gb", "SKU-SSD-BX500-500GB", "Crucial", 950000m, 18, "https://example.com/ssd2.jpg", ComponentType.Storage,
            new StorageSpecification
            {
                StorageType = StorageType.SATA_SSD,
                Interface = StorageInterface.SATA_3,
                CapacityGb = 500,
                ReadSpeedMBs = 540,
                WriteSpeedMBs = 500
            });

        // 5. PSU
        var psu1 = new Product(psuCat.Id, "Corsair RM850x Shift 850W", "corsair-rm850x-shift", "SKU-PSU-RM850X", "Corsair", 3900000m, 6, "https://example.com/psu1.jpg", ComponentType.Psu,
            new PsuSpecification
            {
                Wattage = 850,
                Efficiency = PsuEfficiency.Plus80Gold,
                Modularity = PsuModularity.FullModular,
                FormFactor = PsuFormFactor.ATX,
                Has12VHPWR = true
            });

        var psu2 = new Product(psuCat.Id, "Cooler Master V650 SFX Gold", "cooler-master-v650-sfx", "SKU-PSU-V650-SFX", "Cooler Master", 2600000m, 4, "https://example.com/psu2.jpg", ComponentType.Psu,
            new PsuSpecification
            {
                Wattage = 650,
                Efficiency = PsuEfficiency.Plus80Bronze,
                Modularity = PsuModularity.SemiModular,
                FormFactor = PsuFormFactor.SFX,
                Has12VHPWR = false
            });

        // 6. Case
        var case1 = new Product(caseCat.Id, "NZXT H5 Flow", "nzxt-h5-flow", "SKU-CASE-H5-FLOW", "NZXT", 2100000m, 7, "https://example.com/case1.jpg", ComponentType.Case,
            new CaseSpecification
            {
                SupportedFormFactors = [MotherboardFormFactor.ATX, MotherboardFormFactor.MicroATX, MotherboardFormFactor.MiniITX],
                MaxGpuLengthMm = 365
            });

        var case2 = new Product(caseCat.Id, "Fractal Design Terra", "fractal-design-terra", "SKU-CASE-TERRA", "Fractal", 4500000m, 3, "https://example.com/case2.jpg", ComponentType.Case,
            new CaseSpecification
            {
                SupportedFormFactors = [MotherboardFormFactor.MiniITX],
                MaxGpuLengthMm = 322
            });

        // 7. Cooler
        var cooler1 = new Product(coolerCat.Id, "NZXT Kraken Elite 360 RGB", "nzxt-kraken-elite-360-rgb", "SKU-COOLER-KRAKEN360", "NZXT", 6800000m, 5, "https://example.com/cooler1.jpg", ComponentType.Cooler,
            new CoolerSpecification
            {
                CoolerType = CoolerType.AIO_360,
                SupportedSockets = [CpuSocket.AM5, CpuSocket.AM4, CpuSocket.LGA1700, CpuSocket.LGA1851],
                HasRgb = true
            });

        var cooler2 = new Product(coolerCat.Id, "Noctua NH-D15 chromax.black", "noctua-nh-d15-chromax-black", "SKU-COOLER-NHD15", "Noctua", 2900000m, 8, "https://example.com/cooler2.jpg", ComponentType.Cooler,
            new CoolerSpecification
            {
                CoolerType = CoolerType.Air,
                SupportedSockets = [CpuSocket.AM4],
                HasRgb = false
            });

        // 8. GPU
        var gpu1 = new Product(gpuCat.Id, "ASUS ROG Strix RTX 4080 Super", "asus-rog-strix-rtx-4080-super", "SKU-GPU-4080S", "ASUS", 32000000m, 4, "https://example.com/gpu1.jpg", ComponentType.Gpu,
            new GpuSpecification
            {
                Chipset = "RTX 4080 Super",
                VramGb = 16,
                Requires12VHPWR = true
            });

        var gpu2 = new Product(gpuCat.Id, "Sapphire Pure RX 7800 XT", "sapphire-pure-rx-7800-xt", "SKU-GPU-7800XT", "Sapphire", 15000000m, 6, "https://example.com/gpu2.jpg", ComponentType.Gpu,
            new GpuSpecification
            {
                Chipset = "RX 7800 XT",
                VramGb = 16,
                Requires12VHPWR = false
            });

        db.Products.AddRange(cpu1, cpu2, mb1, mb2, ram1, ram2, storage1, storage2, psu1, psu2, case1, case2, cooler1, cooler2, gpu1, gpu2);
        await db.SaveChangesAsync();

        return new SeededSpecsCatalog(
            cpuCat, mbCat, ramCat, storageCat, psuCat, caseCat, coolerCat, gpuCat,
            cpu1, cpu2, mb1, mb2, ram1, ram2, storage1, storage2, psu1, psu2, case1, case2, cooler1, cooler2, gpu1, gpu2);
    }

    [Fact]
    public void SqlTranslation_DemonstratesJsonbContainmentOperatorAtPostgres()
    {
        using var scope = Fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var sql = db.Products
            .Where(p => EF.Functions.JsonContains(p.Specifications, "{\"Socket\": 5}"))
            .ToQueryString();

        // Demonstrates JSONB @> containment operator translation without client evaluation
        sql.Should().Contain("@>");
    }

    [Fact]
    public async Task GetProducts_CpuFilters_SocketAndIntegratedGpuAndMemoryType()
    {
        var catalog = await SeedSpecificationCatalogAsync();
        using var client = CreatePublicClient();

        // Filter CPU with Socket AM5 -> cpu1
        var resp1 = await client.GetAsync("/api/v1/products?componentType=1&socket=5");
        resp1.StatusCode.Should().Be(HttpStatusCode.OK);
        var res1 = (await resp1.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        res1.TotalCount.Should().Be(1);
        res1.Items.Single().Id.Should().Be(catalog.Cpu1.Id);

        // Filter CPU with hasIntegratedGpu = false -> cpu2
        var resp2 = await client.GetAsync("/api/v1/products?componentType=1&hasIntegratedGpu=false");
        resp2.StatusCode.Should().Be(HttpStatusCode.OK);
        var res2 = (await resp2.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        res2.TotalCount.Should().Be(1);
        res2.Items.Single().Id.Should().Be(catalog.Cpu2.Id);

        // Combined AND: CPU with socket=5 & hasIntegratedGpu=true -> cpu1
        var resp3 = await client.GetAsync("/api/v1/products?componentType=1&socket=5&hasIntegratedGpu=true");
        resp3.StatusCode.Should().Be(HttpStatusCode.OK);
        var res3 = (await resp3.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        res3.TotalCount.Should().Be(1);
        res3.Items.Single().Id.Should().Be(catalog.Cpu1.Id);

        // Combined AND with conflict: CPU with socket=5 & hasIntegratedGpu=false -> 0 matches
        var resp4 = await client.GetAsync("/api/v1/products?componentType=1&socket=5&hasIntegratedGpu=false");
        resp4.StatusCode.Should().Be(HttpStatusCode.OK);
        var res4 = (await resp4.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        res4.TotalCount.Should().Be(0);
        res4.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetProducts_MotherboardFilters_Socket_MemoryType_FormFactor_Wifi()
    {
        var catalog = await SeedSpecificationCatalogAsync();
        using var client = CreatePublicClient();

        // MB with AM5 + DDR5 + ATX + Wifi -> mb1
        var resp1 = await client.GetAsync("/api/v1/products?componentType=2&socket=5&memoryType=2&motherboardFormFactor=1&hasWifi=true");
        resp1.StatusCode.Should().Be(HttpStatusCode.OK);
        var res1 = (await resp1.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        res1.TotalCount.Should().Be(1);
        res1.Items.Single().Id.Should().Be(catalog.Mb1.Id);

        // MB with LGA1700 + DDR4 + MicroATX + No Wifi -> mb2
        var resp2 = await client.GetAsync("/api/v1/products?componentType=2&socket=1&memoryType=1&motherboardFormFactor=2&hasWifi=false");
        resp2.StatusCode.Should().Be(HttpStatusCode.OK);
        var res2 = (await resp2.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        res2.TotalCount.Should().Be(1);
        res2.Items.Single().Id.Should().Be(catalog.Mb2.Id);
    }

    [Fact]
    public async Task GetProducts_RamFilters_MemoryType_FormFactor_Rgb()
    {
        var catalog = await SeedSpecificationCatalogAsync();
        using var client = CreatePublicClient();

        // DDR5 + DIMM + RGB -> ram1
        var resp1 = await client.GetAsync("/api/v1/products?componentType=4&memoryType=2&ramFormFactor=1&hasRgb=true");
        resp1.StatusCode.Should().Be(HttpStatusCode.OK);
        var res1 = (await resp1.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        res1.TotalCount.Should().Be(1);
        res1.Items.Single().Id.Should().Be(catalog.Ram1.Id);

        // DDR4 + DIMM + Non-RGB -> ram2
        var resp2 = await client.GetAsync("/api/v1/products?componentType=4&memoryType=1&ramFormFactor=1&hasRgb=false");
        resp2.StatusCode.Should().Be(HttpStatusCode.OK);
        var res2 = (await resp2.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        res2.TotalCount.Should().Be(1);
        res2.Items.Single().Id.Should().Be(catalog.Ram2.Id);
    }

    [Fact]
    public async Task GetProducts_StorageFilters_StorageType_StorageInterface()
    {
        var catalog = await SeedSpecificationCatalogAsync();
        using var client = CreatePublicClient();

        // NVMe_M2 (1) + PCIe_4_0 (2) -> storage1
        var resp1 = await client.GetAsync("/api/v1/products?componentType=5&storageType=1&storageInterface=2");
        resp1.StatusCode.Should().Be(HttpStatusCode.OK);
        var res1 = (await resp1.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        res1.TotalCount.Should().Be(1);
        res1.Items.Single().Id.Should().Be(catalog.Storage1.Id);

        // SATA_SSD (2) + SATA_3 (4) -> storage2
        var resp2 = await client.GetAsync("/api/v1/products?componentType=5&storageType=2&storageInterface=4");
        resp2.StatusCode.Should().Be(HttpStatusCode.OK);
        var res2 = (await resp2.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        res2.TotalCount.Should().Be(1);
        res2.Items.Single().Id.Should().Be(catalog.Storage2.Id);
    }

    [Fact]
    public async Task GetProducts_PsuFilters_Efficiency_Modularity_FormFactor_12VHPWR()
    {
        var catalog = await SeedSpecificationCatalogAsync();
        using var client = CreatePublicClient();

        // Plus80Gold (4) + FullModular (3) + ATX (1) + Has12VHPWR (true) -> psu1
        var resp1 = await client.GetAsync("/api/v1/products?componentType=6&psuEfficiency=4&psuModularity=3&psuFormFactor=1&has12VHPWR=true");
        resp1.StatusCode.Should().Be(HttpStatusCode.OK);
        var res1 = (await resp1.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        res1.TotalCount.Should().Be(1);
        res1.Items.Single().Id.Should().Be(catalog.Psu1.Id);

        // Plus80Bronze (2) + SemiModular (2) + SFX (2) + Has12VHPWR (false) -> psu2
        var resp2 = await client.GetAsync("/api/v1/products?componentType=6&psuEfficiency=2&psuModularity=2&psuFormFactor=2&has12VHPWR=false");
        resp2.StatusCode.Should().Be(HttpStatusCode.OK);
        var res2 = (await resp2.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        res2.TotalCount.Should().Be(1);
        res2.Items.Single().Id.Should().Be(catalog.Psu2.Id);
    }

    [Fact]
    public async Task GetProducts_CaseFilters_SupportedFormFactors_ArrayContainment()
    {
        var catalog = await SeedSpecificationCatalogAsync();
        using var client = CreatePublicClient();

        // Case supporting ATX (1) -> case1 (case2 only supports MiniITX 3)
        var resp1 = await client.GetAsync("/api/v1/products?componentType=7&motherboardFormFactor=1");
        resp1.StatusCode.Should().Be(HttpStatusCode.OK);
        var res1 = (await resp1.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        res1.TotalCount.Should().Be(1);
        res1.Items.Single().Id.Should().Be(catalog.Case1.Id);

        // Case supporting MiniITX (3) -> both case1 and case2
        var resp2 = await client.GetAsync("/api/v1/products?componentType=7&motherboardFormFactor=3");
        resp2.StatusCode.Should().Be(HttpStatusCode.OK);
        var res2 = (await resp2.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        res2.TotalCount.Should().Be(2);
        res2.Items.Select(p => p.Id).Should().BeEquivalentTo([catalog.Case1.Id, catalog.Case2.Id]);
    }

    [Fact]
    public async Task GetProducts_CoolerFilters_SupportedSocketsArray_CoolerType_Rgb()
    {
        var catalog = await SeedSpecificationCatalogAsync();
        using var client = CreatePublicClient();

        // Cooler supporting AM5 (5) -> cooler1 (Kraken)
        var resp1 = await client.GetAsync("/api/v1/products?componentType=8&socket=5");
        resp1.StatusCode.Should().Be(HttpStatusCode.OK);
        var res1 = (await resp1.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        res1.TotalCount.Should().Be(1);
        res1.Items.Single().Id.Should().Be(catalog.Cooler1.Id);

        // Cooler supporting AM4 (4) -> both cooler1 and cooler2
        var resp2 = await client.GetAsync("/api/v1/products?componentType=8&socket=4");
        resp2.StatusCode.Should().Be(HttpStatusCode.OK);
        var res2 = (await resp2.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        res2.TotalCount.Should().Be(2);

        // Cooler with CoolerType Air (1) + No RGB -> cooler2
        var resp3 = await client.GetAsync("/api/v1/products?componentType=8&coolerType=1&hasRgb=false");
        resp3.StatusCode.Should().Be(HttpStatusCode.OK);
        var res3 = (await resp3.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        res3.TotalCount.Should().Be(1);
        res3.Items.Single().Id.Should().Be(catalog.Cooler2.Id);
    }

    [Fact]
    public async Task GetProducts_GpuFilters_Requires12VHPWR()
    {
        var catalog = await SeedSpecificationCatalogAsync();
        using var client = CreatePublicClient();

        // GPU requires 12VHPWR = true -> gpu1
        var resp1 = await client.GetAsync("/api/v1/products?componentType=3&requires12VHPWR=true");
        resp1.StatusCode.Should().Be(HttpStatusCode.OK);
        var res1 = (await resp1.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        res1.TotalCount.Should().Be(1);
        res1.Items.Single().Id.Should().Be(catalog.Gpu1.Id);

        // GPU requires 12VHPWR = false -> gpu2
        var resp2 = await client.GetAsync("/api/v1/products?componentType=3&requires12VHPWR=false");
        resp2.StatusCode.Should().Be(HttpStatusCode.OK);
        var res2 = (await resp2.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        res2.TotalCount.Should().Be(1);
        res2.Items.Single().Id.Should().Be(catalog.Gpu2.Id);
    }

    [Fact]
    public async Task GetProducts_PaginationAndSorting_AppliedAfterSpecificationFiltering()
    {
        var catalog = await SeedSpecificationCatalogAsync();
        using var client = CreatePublicClient();

        // Both cooler1 (6.8M) and cooler2 (2.9M) support socket AM4 (4)
        // PageSize = 1, SortBy = PriceLowToHigh (2) -> Page 1 should return Cooler2, TotalCount = 2, TotalPages = 2
        var page1Resp = await client.GetAsync("/api/v1/products?componentType=8&socket=4&pageIndex=1&pageSize=1&sortBy=2");
        page1Resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var page1Result = (await page1Resp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        page1Result.TotalCount.Should().Be(2);
        page1Result.TotalPages.Should().Be(2);
        page1Result.Items.Should().HaveCount(1);
        page1Result.Items.Single().Id.Should().Be(catalog.Cooler2.Id);

        // Page 2 should return Cooler1
        var page2Resp = await client.GetAsync("/api/v1/products?componentType=8&socket=4&pageIndex=2&pageSize=1&sortBy=2");
        page2Resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var page2Result = (await page2Resp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        page2Result.TotalCount.Should().Be(2);
        page2Result.Items.Should().HaveCount(1);
        page2Result.Items.Single().Id.Should().Be(catalog.Cooler1.Id);
    }

    [Fact]
    public async Task GetProducts_InvalidApplicability_Returns400BadRequest()
    {
        await SeedSpecificationCatalogAsync();
        using var client = CreatePublicClient();

        // Socket filter applied to RAM component type -> 400
        var resp1 = await client.GetAsync("/api/v1/products?componentType=4&socket=5");
        resp1.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Technical filter without componentType -> 400
        var resp2 = await client.GetAsync("/api/v1/products?hasIntegratedGpu=true");
        resp2.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // HasWifi applied to GPU -> 400
        var resp3 = await client.GetAsync("/api/v1/products?componentType=3&hasWifi=true");
        resp3.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}

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
                LengthMm = 304,
                Requires12VHPWR = true
            });

        var gpu2 = new Product(gpuCat.Id, "Sapphire Pure RX 7800 XT", "sapphire-pure-rx-7800-xt", "SKU-GPU-7800XT", "Sapphire", 15000000m, 6, "https://example.com/gpu2.jpg", ComponentType.Gpu,
            new GpuSpecification
            {
                Chipset = "RX 7800 XT",
                VramGb = 16,
                LengthMm = 267,
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

    [Fact]
    public async Task GetProducts_MultiSelect_UnionWithinSameGroup_IntersectionAcrossGroupsAndPrice()
    {
        var catalog = await SeedSpecificationCatalogAsync();
        using var client = CreatePublicClient();

        // 1. Two brands in same list => OR union
        // AMD (cpu1) + Intel (cpu2) -> both returned (TotalCount = 2)
        var brandResp = await client.GetAsync("/api/v1/products?componentType=1&brands=AMD&brands=Intel");
        brandResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var brandResult = (await brandResp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        brandResult.TotalCount.Should().Be(2);
        brandResult.Items.Select(x => x.Id).Should().BeEquivalentTo([catalog.Cpu1.Id, catalog.Cpu2.Id]);

        // 2. Two sockets in same list => OR union
        // AM5 (5) + LGA1700 (1) -> both returned (TotalCount = 2)
        var socketResp = await client.GetAsync("/api/v1/products?componentType=1&sockets=5&sockets=1");
        socketResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var socketResult = (await socketResp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        socketResult.TotalCount.Should().Be(2);
        socketResult.Items.Select(x => x.Id).Should().BeEquivalentTo([catalog.Cpu1.Id, catalog.Cpu2.Id]);

        // 3. Both brand list and socket list => AND intersection between groups
        // (AMD OR Intel) AND (AM5 OR LGA1700) -> both returned
        var bothResp = await client.GetAsync("/api/v1/products?componentType=1&brands=AMD&brands=Intel&sockets=5&sockets=1");
        bothResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var bothResult = (await bothResp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        bothResult.TotalCount.Should().Be(2);

        // 4. Brand AMD AND LGA1700 (1) -> 0 results (intersection between groups)
        var conflictResp = await client.GetAsync("/api/v1/products?componentType=1&brands=AMD&sockets=1");
        conflictResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var conflictResult = (await conflictResp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        conflictResult.TotalCount.Should().Be(0);
        conflictResult.Items.Should().BeEmpty();

        // 5. Union of brands/sockets AND minPrice intersection
        // (AMD OR Intel) AND (AM5 OR LGA1700) AND minPrice >= 4,000,000 -> only Cpu1 (5,000,000)
        var priceResp = await client.GetAsync("/api/v1/products?componentType=1&brands=AMD&brands=Intel&sockets=5&sockets=1&minPrice=4000000");
        priceResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var priceResult = (await priceResp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        priceResult.TotalCount.Should().Be(1);
        priceResult.Items.Single().Id.Should().Be(catalog.Cpu1.Id);
    }

    [Fact]
    public async Task GetProducts_ScalarAndListUnion_DeduplicatesAndPreservesLegacyScalar()
    {
        var catalog = await SeedSpecificationCatalogAsync();
        using var client = CreatePublicClient();

        // Scalar brand + list brands with casing/duplicates: brand=amd&brands=AMD&brands=amd
        // Should merge to single "amd" search, matching Cpu1 (AMD) without duplicating rows
        var brandResp = await client.GetAsync("/api/v1/products?componentType=1&brand=amd&brands=AMD&brands=amd");
        brandResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var brandResult = (await brandResp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        brandResult.TotalCount.Should().Be(1);
        brandResult.Items.Single().Id.Should().Be(catalog.Cpu1.Id);

        // Scalar socket + list sockets: socket=5&sockets=5&sockets=1
        // Merged to [5, 1], returning both Cpu1 and Cpu2 without duplicating Cpu1
        var socketResp = await client.GetAsync("/api/v1/products?componentType=1&socket=5&sockets=5&sockets=1");
        socketResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var socketResult = (await socketResp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        socketResult.TotalCount.Should().Be(2);
        socketResult.Items.Select(x => x.Id).Should().BeEquivalentTo([catalog.Cpu1.Id, catalog.Cpu2.Id]);

        // Legacy scalar: socket=5
        var legacyResp = await client.GetAsync("/api/v1/products?componentType=1&socket=5");
        legacyResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var legacyResult = (await legacyResp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        legacyResult.TotalCount.Should().Be(1);
        legacyResult.Items.Single().Id.Should().Be(catalog.Cpu1.Id);
    }

    [Fact]
    public async Task GetProducts_CoolerAndCase_ArrayContainment_AnyMatchNotAll_NoRowDuplication()
    {
        var catalog = await SeedSpecificationCatalogAsync();
        using var client = CreatePublicClient();

        // Cooler1 supports [AM5 (5), AM4 (4), LGA1700 (1), LGA1851 (2)]
        // Cooler2 supports [AM4 (4)]
        // Query sockets=5&sockets=4: Cooler1 matches BOTH, Cooler2 matches AM4.
        // Array ANY semantics: both match, Cooler1 must appear exactly ONCE (not twice).
        var coolerResp = await client.GetAsync("/api/v1/products?componentType=8&sockets=5&sockets=4");
        coolerResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var coolerResult = (await coolerResp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        coolerResult.TotalCount.Should().Be(2);
        coolerResult.Items.Should().HaveCount(2);
        coolerResult.Items.Select(x => x.Id).Should().BeEquivalentTo([catalog.Cooler1.Id, catalog.Cooler2.Id]);

        // Case1 supports [ATX (1), MicroATX (2), MiniITX (3)]
        // Case2 supports [MiniITX (3)]
        // Query motherboardFormFactors=1&motherboardFormFactors=3: Case1 matches BOTH, Case2 matches MiniITX.
        // Array ANY semantics: both match, Case1 must appear exactly ONCE.
        var caseResp = await client.GetAsync("/api/v1/products?componentType=7&motherboardFormFactors=1&motherboardFormFactors=3");
        caseResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var caseResult = (await caseResp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        caseResult.TotalCount.Should().Be(2);
        caseResult.Items.Should().HaveCount(2);
        caseResult.Items.Select(x => x.Id).Should().BeEquivalentTo([catalog.Case1.Id, catalog.Case2.Id]);
    }

    [Fact]
    public async Task GetProducts_AllSevenNumericGroups_ExactBoundaryAndFieldSeparation()
    {
        var catalog = await SeedSpecificationCatalogAsync();
        using var client = CreatePublicClient();

        // 1. CPU CoreCounts: Cpu1 (6), Cpu2 (6)
        var coreResp1 = await client.GetAsync("/api/v1/products?componentType=1&coreCounts=6");
        coreResp1.StatusCode.Should().Be(HttpStatusCode.OK);
        var coreResult1 = (await coreResp1.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        coreResult1.TotalCount.Should().Be(2);

        var coreResp2 = await client.GetAsync("/api/v1/products?componentType=1&coreCounts=8");
        coreResp2.StatusCode.Should().Be(HttpStatusCode.OK);
        var coreResult2 = (await coreResp2.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        coreResult2.TotalCount.Should().Be(0);

        // 2. GPU VramGbs: Gpu1 (16), Gpu2 (16)
        var vramResp1 = await client.GetAsync("/api/v1/products?componentType=3&vramGbs=16");
        vramResp1.StatusCode.Should().Be(HttpStatusCode.OK);
        var vramResult1 = (await vramResp1.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        vramResult1.TotalCount.Should().Be(2);

        var vramResp2 = await client.GetAsync("/api/v1/products?componentType=3&vramGbs=24");
        vramResp2.StatusCode.Should().Be(HttpStatusCode.OK);
        var vramResult2 = (await vramResp2.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        vramResult2.TotalCount.Should().Be(0);

        // 3. GPU LengthsMm vs Case MaxGpuLengthMm separation:
        // Gpu1 has LengthMm = 304, Gpu2 has LengthMm = 267. Case1 has MaxGpuLengthMm = 365.
        var gpuLenResp1 = await client.GetAsync("/api/v1/products?componentType=3&gpuLengthsMm=304");
        gpuLenResp1.StatusCode.Should().Be(HttpStatusCode.OK);
        var gpuLenResult1 = (await gpuLenResp1.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        gpuLenResult1.TotalCount.Should().Be(1);
        gpuLenResult1.Items.Single().Id.Should().Be(catalog.Gpu1.Id);

        // Querying gpuLengthsMm=365 for GPU should NOT match Case1
        var gpuLenResp2 = await client.GetAsync("/api/v1/products?componentType=3&gpuLengthsMm=365");
        gpuLenResp2.StatusCode.Should().Be(HttpStatusCode.OK);
        var gpuLenResult2 = (await gpuLenResp2.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        gpuLenResult2.TotalCount.Should().Be(0);

        // Applying gpuLengthsMm to Case should fail validation (400)
        var gpuLenCaseResp = await client.GetAsync("/api/v1/products?componentType=7&gpuLengthsMm=365");
        gpuLenCaseResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 4. RAM CapacitiesGb (total kit capacity): Ram1 (32GB, 2 modules), Ram2 (16GB)
        var ramCapResp1 = await client.GetAsync("/api/v1/products?componentType=4&ramCapacitiesGb=32");
        ramCapResp1.StatusCode.Should().Be(HttpStatusCode.OK);
        var ramCapResult1 = (await ramCapResp1.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        ramCapResult1.TotalCount.Should().Be(1);
        ramCapResult1.Items.Single().Id.Should().Be(catalog.Ram1.Id);

        var ramCapResp2 = await client.GetAsync("/api/v1/products?componentType=4&ramCapacitiesGb=16");
        ramCapResp2.StatusCode.Should().Be(HttpStatusCode.OK);
        var ramCapResult2 = (await ramCapResp2.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        ramCapResult2.TotalCount.Should().Be(1);
        ramCapResult2.Items.Single().Id.Should().Be(catalog.Ram2.Id);

        // 5. RAM SpeedsMhz: Ram1 (6000), Ram2 (3200)
        var ramSpdResp1 = await client.GetAsync("/api/v1/products?componentType=4&ramSpeedsMhz=6000");
        ramSpdResp1.StatusCode.Should().Be(HttpStatusCode.OK);
        var ramSpdResult1 = (await ramSpdResp1.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        ramSpdResult1.TotalCount.Should().Be(1);
        ramSpdResult1.Items.Single().Id.Should().Be(catalog.Ram1.Id);

        var ramSpdResp2 = await client.GetAsync("/api/v1/products?componentType=4&ramSpeedsMhz=5200");
        ramSpdResp2.StatusCode.Should().Be(HttpStatusCode.OK);
        var ramSpdResult2 = (await ramSpdResp2.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        ramSpdResult2.TotalCount.Should().Be(0);

        // 6. Storage CapacitiesGb: Storage1 (1000), Storage2 (500)
        var storCapResp1 = await client.GetAsync("/api/v1/products?componentType=5&storageCapacitiesGb=1000");
        storCapResp1.StatusCode.Should().Be(HttpStatusCode.OK);
        var storCapResult1 = (await storCapResp1.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        storCapResult1.TotalCount.Should().Be(1);
        storCapResult1.Items.Single().Id.Should().Be(catalog.Storage1.Id);

        var storCapResp2 = await client.GetAsync("/api/v1/products?componentType=5&storageCapacitiesGb=2000");
        storCapResp2.StatusCode.Should().Be(HttpStatusCode.OK);
        var storCapResult2 = (await storCapResp2.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        storCapResult2.TotalCount.Should().Be(0);

        // 7. PSU Wattages: Psu1 (850), Psu2 (650)
        var psuWatResp1 = await client.GetAsync("/api/v1/products?componentType=6&psuWattages=850");
        psuWatResp1.StatusCode.Should().Be(HttpStatusCode.OK);
        var psuWatResult1 = (await psuWatResp1.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        psuWatResult1.TotalCount.Should().Be(1);
        psuWatResult1.Items.Single().Id.Should().Be(catalog.Psu1.Id);

        var psuWatResp2 = await client.GetAsync("/api/v1/products?componentType=6&psuWattages=1000");
        psuWatResp2.StatusCode.Should().Be(HttpStatusCode.OK);
        var psuWatResult2 = (await psuWatResp2.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        psuWatResult2.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task GetProducts_PaginationAndCount_FiltersAcrossEntireDataset_NoOverlap()
    {
        using var scope = Fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var cat = new Category("Pagination Test Category", $"pagination-cat-{Guid.NewGuid():N}", ComponentType.Storage);
        db.Categories.Add(cat);
        await db.SaveChangesAsync();

        var products = new List<Product>();
        // 15 products with CapacityGb = 1000
        for (int i = 1; i <= 15; i++)
        {
            products.Add(new Product(
                cat.Id,
                $"Storage 1TB #{i:D2}",
                $"storage-1tb-{Guid.NewGuid():N}",
                $"SKU-1TB-{i:D2}-{Guid.NewGuid():N}",
                "Crucial",
                1000000m + (i * 10000m),
                10,
                $"https://example.com/p1tb{i}.jpg",
                ComponentType.Storage,
                new StorageSpecification
                {
                    StorageType = StorageType.NVMe_M2,
                    Interface = StorageInterface.PCIe_4_0,
                    CapacityGb = 1000
                }));
        }
        // 10 products with CapacityGb = 2000
        for (int i = 1; i <= 10; i++)
        {
            products.Add(new Product(
                cat.Id,
                $"Storage 2TB #{i:D2}",
                $"storage-2tb-{Guid.NewGuid():N}",
                $"SKU-2TB-{i:D2}-{Guid.NewGuid():N}",
                "Crucial",
                3000000m + (i * 10000m),
                10,
                $"https://example.com/p2tb{i}.jpg",
                ComponentType.Storage,
                new StorageSpecification
                {
                    StorageType = StorageType.NVMe_M2,
                    Interface = StorageInterface.PCIe_4_0,
                    CapacityGb = 2000
                }));
        }
        db.Products.AddRange(products);
        await db.SaveChangesAsync();

        using var client = CreatePublicClient();

        // Page 1: pageSize = 10, filter storageCapacitiesGb = 1000, sortBy = PriceLowToHigh (2)
        var page1Resp = await client.GetAsync($"/api/v1/products?categoryId={cat.Id}&componentType=5&storageCapacitiesGb=1000&pageIndex=1&pageSize=10&sortBy=2");
        page1Resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var page1Result = (await page1Resp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        page1Result.TotalCount.Should().Be(15);
        page1Result.TotalPages.Should().Be(2);
        page1Result.Items.Should().HaveCount(10);

        // Page 2: pageSize = 10, filter storageCapacitiesGb = 1000
        var page2Resp = await client.GetAsync($"/api/v1/products?categoryId={cat.Id}&componentType=5&storageCapacitiesGb=1000&pageIndex=2&pageSize=10&sortBy=2");
        page2Resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var page2Result = (await page2Resp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        page2Result.TotalCount.Should().Be(15);
        page2Result.TotalPages.Should().Be(2);
        page2Result.Items.Should().HaveCount(5);

        // Overlap verification: Page 1 and Page 2 must not overlap
        var page1Ids = page1Result.Items.Select(x => x.Id).ToHashSet();
        var page2Ids = page2Result.Items.Select(x => x.Id).ToHashSet();
        page1Ids.Intersect(page2Ids).Should().BeEmpty();
        page1Ids.Union(page2Ids).Should().HaveCount(15);
    }

    [Fact]
    public async Task GetFilterOptions_ScopedCategoryAndDescendant_ComponentType_ActiveStock_SortedAndDeduped()
    {
        using var scope = Fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var parentCat = new Category("Root CPU FilterOptions", $"root-cpu-fo-{Guid.NewGuid():N}", ComponentType.Cpu);
        db.Categories.Add(parentCat);
        await db.SaveChangesAsync();

        var childCat = new Category("Child Desktop CPUs", $"child-cpu-fo-{Guid.NewGuid():N}", ComponentType.Cpu, parentId: parentCat.Id);
        db.Categories.Add(childCat);
        await db.SaveChangesAsync();

        // Product A: Brand "  intel  " (spaces), Active, InStock, LGA1700 (1), 8 cores
        var prodA = new Product(childCat.Id, "Intel Core i7-14700", $"intel-14700-{Guid.NewGuid():N}", $"SKU-A-{Guid.NewGuid():N}", "  intel  ", 9000000m, 10, "https://example.com/a.jpg", ComponentType.Cpu,
            new CpuSpecification { Socket = CpuSocket.LGA1700, CoreCount = 8 });

        // Product B: Brand "Intel" (PascalCase), Active, InStock, LGA1700 (1), 16 cores
        var prodB = new Product(childCat.Id, "Intel Core i9-14900", $"intel-14900-{Guid.NewGuid():N}", $"SKU-B-{Guid.NewGuid():N}", "Intel", 14000000m, 5, "https://example.com/b.jpg", ComponentType.Cpu,
            new CpuSpecification { Socket = CpuSocket.LGA1700, CoreCount = 16 });

        // Product C: Brand "AMD", Inactive, InStock, AM5 (5), 12 cores
        var prodC = new Product(childCat.Id, "AMD Ryzen 9 7900", $"amd-7900-{Guid.NewGuid():N}", $"SKU-C-{Guid.NewGuid():N}", "AMD", 10000000m, 5, "https://example.com/c.jpg", ComponentType.Cpu,
            new CpuSpecification { Socket = CpuSocket.AM5, CoreCount = 12 });
        prodC.IsActive = false;

        // Product D: Brand "AMD", Active, OutOfStock (0), AM5 (5), 6 cores
        var prodD = new Product(childCat.Id, "AMD Ryzen 5 7500F", $"amd-7500f-{Guid.NewGuid():N}", $"SKU-D-{Guid.NewGuid():N}", "AMD", 3800000m, 0, "https://example.com/d.jpg", ComponentType.Cpu,
            new CpuSpecification { Socket = CpuSocket.AM5, CoreCount = 6 });

        db.Products.AddRange(prodA, prodB, prodC, prodD);
        await db.SaveChangesAsync();

        using var client = CreatePublicClient();

        // 1. Anonymous access & descendant scope: CategoryId = parentCat.Id includes childCat products
        var resp1 = await client.GetAsync($"/api/v1/products/filter-options?categoryId={parentCat.Id}&componentType=1");
        resp1.StatusCode.Should().Be(HttpStatusCode.OK);
        var options1 = (await resp1.Content.ReadFromJsonAsync<ProductFilterOptionsDto>(JsonOptions))!;

        // Brands: trimmed, deduped case-insensitively, sorted -> AMD, intel (or Intel)
        options1.Brands.Should().HaveCount(2);
        options1.Brands.Select(b => b.ToLowerInvariant()).Should().Contain(["amd", "intel"]);

        // Groups for CPU whitelist: sockets, memoryTypes, coreCounts
        options1.Groups.Select(g => g.Key).Should().BeEquivalentTo(["sockets", "memoryTypes", "coreCounts"]);
        var socketsGroup = options1.Groups.Single(g => g.Key == "sockets");
        socketsGroup.Values.Should().BeEquivalentTo([1, 5]); // LGA1700 (1), AM5 (5)
        var coresGroup = options1.Groups.Single(g => g.Key == "coreCounts");
        coresGroup.Values.Should().BeEquivalentTo([6, 8, 12, 16]); // All 4 products included by default

        // 2. Missing category -> 404
        var missingCatResp = await client.GetAsync($"/api/v1/products/filter-options?categoryId={Guid.NewGuid()}");
        missingCatResp.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // 3. No componentType -> returns brands only, groups is empty
        var noCompResp = await client.GetAsync($"/api/v1/products/filter-options?categoryId={parentCat.Id}");
        noCompResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var noCompOptions = (await noCompResp.Content.ReadFromJsonAsync<ProductFilterOptionsDto>(JsonOptions))!;
        noCompOptions.Brands.Should().HaveCount(2);
        noCompOptions.Groups.Should().BeEmpty();

        // 4. Scoped by isActive=true & inStock=true:
        // Excludes Prod C (inactive) and Prod D (out of stock).
        // Only Prod A & Prod B remain -> only "Intel", sockets [1], coreCounts [8, 16]
        var scopedResp = await client.GetAsync($"/api/v1/products/filter-options?categoryId={parentCat.Id}&componentType=1&isActive=true&inStock=true");
        scopedResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var scopedOptions = (await scopedResp.Content.ReadFromJsonAsync<ProductFilterOptionsDto>(JsonOptions))!;
        scopedOptions.Brands.Should().HaveCount(1);
        scopedOptions.Brands.Single().ToLowerInvariant().Should().Be("intel");
        scopedOptions.Groups.Single(g => g.Key == "sockets").Values.Should().BeEquivalentTo([1]);
        scopedOptions.Groups.Single(g => g.Key == "coreCounts").Values.Should().BeEquivalentTo([8, 16]);

        // 5. Empty scope (category with no products) -> returns 200 with empty arrays
        var emptyCat = new Category("Empty CPU Cat", $"empty-cpu-cat-{Guid.NewGuid():N}", ComponentType.Cpu);
        db.Categories.Add(emptyCat);
        await db.SaveChangesAsync();

        var emptyResp = await client.GetAsync($"/api/v1/products/filter-options?categoryId={emptyCat.Id}&componentType=1");
        emptyResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var emptyOptions = (await emptyResp.Content.ReadFromJsonAsync<ProductFilterOptionsDto>(JsonOptions))!;
        emptyOptions.Brands.Should().BeEmpty();
        emptyOptions.Groups.Should().HaveCount(3);
        emptyOptions.Groups.All(g => g.Values.Count == 0).Should().BeTrue();
    }

    [Fact]
    public async Task GetFilterOptions_FacetOptionsSentBackToProductsQuery_ReturnsExpectedResults()
    {
        await SeedSpecificationCatalogAsync();
        using var client = CreatePublicClient();

        // Retrieve filter options for CPUs
        var optionsResp = await client.GetAsync("/api/v1/products/filter-options?componentType=1");
        optionsResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var options = (await optionsResp.Content.ReadFromJsonAsync<ProductFilterOptionsDto>(JsonOptions))!;
        options.Brands.Should().NotBeEmpty();

        // Every brand option should return matching products when passed to /api/v1/products
        foreach (var brand in options.Brands)
        {
            var prodResp = await client.GetAsync($"/api/v1/products?componentType=1&brands={Uri.EscapeDataString(brand)}");
            prodResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var prodResult = (await prodResp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
            prodResult.TotalCount.Should().BeGreaterThan(0);
            prodResult.Items.All(p => p.Brand.Trim().Equals(brand.Trim(), StringComparison.OrdinalIgnoreCase)).Should().BeTrue();
        }

        // Every socket option in sockets group should return matching products
        var socketGroup = options.Groups.FirstOrDefault(g => g.Key == "sockets");
        socketGroup.Should().NotBeNull();
        foreach (var socketVal in socketGroup!.Values)
        {
            var prodResp = await client.GetAsync($"/api/v1/products?componentType=1&sockets={socketVal}");
            prodResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var prodResult = (await prodResp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
            prodResult.TotalCount.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public async Task GetFilterOptions_RouteMetadata_NotInterceptedByIdOrSlugRoute()
    {
        var catalog = await SeedSpecificationCatalogAsync();
        using var client = CreatePublicClient();

        // 1. GET /api/v1/products/filter-options without auth token -> 200 OK (public, not 401, not 404)
        var optionsResp = await client.GetAsync("/api/v1/products/filter-options");
        optionsResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. GET /api/v1/products/{id} without auth -> 401 Unauthorized (managed endpoint permissions preserved)
        var unauthIdResp = await client.GetAsync($"/api/v1/products/{catalog.Cpu1.Id}");
        unauthIdResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // 3. GET /api/v1/products/{id} with admin client -> 200 OK
        using var adminClient = CreateAdminClient();
        var adminIdResp = await adminClient.GetAsync($"/api/v1/products/{catalog.Cpu1.Id}");
        adminIdResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. GET /api/v1/products/slug/{slug} with public client -> 200 OK
        var slugResp = await client.GetAsync($"/api/v1/products/slug/{catalog.Cpu1.Slug}");
        slugResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 5. GET /api/v1/products/slug/filter-options -> 404 (slug does not exist)
        var slugOptionsResp = await client.GetAsync("/api/v1/products/slug/filter-options");
        slugOptionsResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

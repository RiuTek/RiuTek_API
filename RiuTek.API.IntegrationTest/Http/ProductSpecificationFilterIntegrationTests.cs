using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using RiuTek.API.IntegrationTest.Infrastructure;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Common.Models;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Products.Queries;
using RiuTek.Core.Entities;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;
using RiuTek.Infrastructure.Data;
using RiuTek.Infrastructure.Services;
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

    internal sealed class TestQueryCaptureInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = new();

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private record NineSubtypesCatalog(
        Category RootCat,
        Dictionary<ComponentType, List<Product>> ProductsByType
    );

    private async Task<NineSubtypesCatalog> SeedNineSubtypesCatalogAsync()
    {
        using var scope = Fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var cat = new Category("Nine Subtypes Root", $"nine-types-{Guid.NewGuid():N}", ComponentType.Cpu);
        db.Categories.Add(cat);
        await db.SaveChangesAsync();

        var productsByType = new Dictionary<ComponentType, List<Product>>();

        // 1. CPU
        var cpu1 = new Product(cat.Id, "AMD Ryzen 5 7600", $"cpu-7600-{Guid.NewGuid():N}", $"SKU-CPU-1-{Guid.NewGuid():N}", "AMD", 5000000m, 10, "https://example.com/c1.jpg", ComponentType.Cpu,
            new CpuSpecification { Socket = CpuSocket.AM5, SupportedMemoryType = RamType.DDR5, CoreCount = 6 });
        var cpu2 = new Product(cat.Id, "AMD Ryzen 7 5700X", $"cpu-5700x-{Guid.NewGuid():N}", $"SKU-CPU-2-{Guid.NewGuid():N}", "  amd  ", 4200000m, 8, "https://example.com/c2.jpg", ComponentType.Cpu,
            new CpuSpecification { Socket = CpuSocket.AM4, SupportedMemoryType = RamType.DDR4, CoreCount = 8 });
        var cpu3 = new Product(cat.Id, "Intel Core i5-14600K", $"cpu-14600k-{Guid.NewGuid():N}", $"SKU-CPU-3-{Guid.NewGuid():N}", "Intel", 7800000m, 5, "https://example.com/c3.jpg", ComponentType.Cpu,
            new CpuSpecification { Socket = CpuSocket.LGA1700, SupportedMemoryType = RamType.DDR5, CoreCount = 14 });
        productsByType[ComponentType.Cpu] = [cpu1, cpu2, cpu3];

        // 2. Motherboard
        var mb1 = new Product(cat.Id, "ASUS ROG STRIX B650E", $"mb-b650e-{Guid.NewGuid():N}", $"SKU-MB-1-{Guid.NewGuid():N}", "ASUS", 7000000m, 6, "https://example.com/m1.jpg", ComponentType.Motherboard,
            new MotherboardSpecification { Socket = CpuSocket.AM5, MemoryType = RamType.DDR5, FormFactor = MotherboardFormFactor.ATX });
        var mb2 = new Product(cat.Id, "MSI PRO B760M-P", $"mb-b760m-{Guid.NewGuid():N}", $"SKU-MB-2-{Guid.NewGuid():N}", "MSI", 3000000m, 12, "https://example.com/m2.jpg", ComponentType.Motherboard,
            new MotherboardSpecification { Socket = CpuSocket.LGA1700, MemoryType = RamType.DDR4, FormFactor = MotherboardFormFactor.MicroATX });
        var mb3 = new Product(cat.Id, "ASUS ROG STRIX B550-I", $"mb-b550i-{Guid.NewGuid():N}", $"SKU-MB-3-{Guid.NewGuid():N}", "asus", 4900000m, 4, "https://example.com/m3.jpg", ComponentType.Motherboard,
            new MotherboardSpecification { Socket = CpuSocket.AM4, MemoryType = RamType.DDR4, FormFactor = MotherboardFormFactor.MiniITX });
        productsByType[ComponentType.Motherboard] = [mb1, mb2, mb3];

        // 3. GPU
        var gpu1 = new Product(cat.Id, "Gigabyte RTX 4070 Ti", $"gpu-4070ti-{Guid.NewGuid():N}", $"SKU-GPU-1-{Guid.NewGuid():N}", "Gigabyte", 21000000m, 4, "https://example.com/g1.jpg", ComponentType.Gpu,
            new GpuSpecification { VramGb = 16, LengthMm = 304 });
        var gpu2 = new Product(cat.Id, "Gigabyte RTX 4070", $"gpu-4070-{Guid.NewGuid():N}", $"SKU-GPU-2-{Guid.NewGuid():N}", "gigabyte", 16000000m, 7, "https://example.com/g2.jpg", ComponentType.Gpu,
            new GpuSpecification { VramGb = 12, LengthMm = 267 });
        var gpu3 = new Product(cat.Id, "ZOTAC RTX 4090", $"gpu-4090-{Guid.NewGuid():N}", $"SKU-GPU-3-{Guid.NewGuid():N}", "ZOTAC", 48000000m, 2, "https://example.com/g3.jpg", ComponentType.Gpu,
            new GpuSpecification { VramGb = 24, LengthMm = 320 });
        productsByType[ComponentType.Gpu] = [gpu1, gpu2, gpu3];

        // 4. RAM
        var ram1 = new Product(cat.Id, "Corsair Vengeance DDR5 32GB", $"ram-ddr5-32-{Guid.NewGuid():N}", $"SKU-RAM-1-{Guid.NewGuid():N}", "Corsair", 3200000m, 10, "https://example.com/r1.jpg", ComponentType.Ram,
            new RamSpecification { MemoryType = RamType.DDR5, FormFactor = RamFormFactor.DIMM, CapacityGb = 32, SpeedMhz = 6000, ModuleCount = 2 });
        var ram2 = new Product(cat.Id, "Kingston Fury DDR4 16GB", $"ram-ddr4-16-{Guid.NewGuid():N}", $"SKU-RAM-2-{Guid.NewGuid():N}", "Kingston", 1200000m, 15, "https://example.com/r2.jpg", ComponentType.Ram,
            new RamSpecification { MemoryType = RamType.DDR4, FormFactor = RamFormFactor.DIMM, CapacityGb = 16, SpeedMhz = 3200, ModuleCount = 2 });
        var ram3 = new Product(cat.Id, "Corsair Vengeance SODIMM 64GB", $"ram-sodimm-64-{Guid.NewGuid():N}", $"SKU-RAM-3-{Guid.NewGuid():N}", "corsair", 5500000m, 5, "https://example.com/r3.jpg", ComponentType.Ram,
            new RamSpecification { MemoryType = RamType.DDR5, FormFactor = RamFormFactor.SODIMM, CapacityGb = 64, SpeedMhz = 5600, ModuleCount = 2 });
        productsByType[ComponentType.Ram] = [ram1, ram2, ram3];

        // 5. Storage
        var st1 = new Product(cat.Id, "Samsung 990 PRO 1TB", $"ssd-990pro-1tb-{Guid.NewGuid():N}", $"SKU-ST-1-{Guid.NewGuid():N}", "Samsung", 2800000m, 20, "https://example.com/s1.jpg", ComponentType.Storage,
            new StorageSpecification { StorageType = StorageType.NVMe_M2, Interface = StorageInterface.PCIe_4_0, CapacityGb = 1000 });
        var st2 = new Product(cat.Id, "Crucial BX500 500GB", $"ssd-bx500-500gb-{Guid.NewGuid():N}", $"SKU-ST-2-{Guid.NewGuid():N}", "Crucial", 950000m, 14, "https://example.com/s2.jpg", ComponentType.Storage,
            new StorageSpecification { StorageType = StorageType.SATA_SSD, Interface = StorageInterface.SATA_3, CapacityGb = 500 });
        var st3 = new Product(cat.Id, "Samsung 990 EVO Plus 2TB", $"ssd-990evo-2tb-{Guid.NewGuid():N}", $"SKU-ST-3-{Guid.NewGuid():N}", "samsung", 4500000m, 8, "https://example.com/s3.jpg", ComponentType.Storage,
            new StorageSpecification { StorageType = StorageType.NVMe_M2, Interface = StorageInterface.PCIe_5_0, CapacityGb = 2000 });
        productsByType[ComponentType.Storage] = [st1, st2, st3];

        // 6. PSU
        var psu1 = new Product(cat.Id, "Seasonic Focus GX-850", $"psu-gx850-{Guid.NewGuid():N}", $"SKU-PSU-1-{Guid.NewGuid():N}", "Seasonic", 3600000m, 6, "https://example.com/p1.jpg", ComponentType.Psu,
            new PsuSpecification { Efficiency = PsuEfficiency.Plus80Gold, Modularity = PsuModularity.FullModular, FormFactor = PsuFormFactor.ATX, Wattage = 850 });
        var psu2 = new Product(cat.Id, "Corsair SF650", $"psu-sf650-{Guid.NewGuid():N}", $"SKU-PSU-2-{Guid.NewGuid():N}", "Corsair", 2800000m, 5, "https://example.com/p2.jpg", ComponentType.Psu,
            new PsuSpecification { Efficiency = PsuEfficiency.Plus80Bronze, Modularity = PsuModularity.SemiModular, FormFactor = PsuFormFactor.SFX, Wattage = 650 });
        var psu3 = new Product(cat.Id, "Seasonic Prime TX-1000", $"psu-tx1000-{Guid.NewGuid():N}", $"SKU-PSU-3-{Guid.NewGuid():N}", "seasonic", 6500000m, 3, "https://example.com/p3.jpg", ComponentType.Psu,
            new PsuSpecification { Efficiency = PsuEfficiency.Plus80Platinum, Modularity = PsuModularity.FullModular, FormFactor = PsuFormFactor.ATX, Wattage = 1000 });
        productsByType[ComponentType.Psu] = [psu1, psu2, psu3];

        // 7. Case
        var case1 = new Product(cat.Id, "Lian Li O11 Dynamic", $"case-o11d-{Guid.NewGuid():N}", $"SKU-CASE-1-{Guid.NewGuid():N}", "Lian Li", 3500000m, 5, "https://example.com/cs1.jpg", ComponentType.Case,
            new CaseSpecification { SupportedFormFactors = [MotherboardFormFactor.ATX, MotherboardFormFactor.MicroATX], MaxGpuLengthMm = 380 });
        var case2 = new Product(cat.Id, "Fractal Design Meshify 2 Nano", $"case-nano-{Guid.NewGuid():N}", $"SKU-CASE-2-{Guid.NewGuid():N}", "Fractal", 2600000m, 4, "https://example.com/cs2.jpg", ComponentType.Case,
            new CaseSpecification { SupportedFormFactors = [MotherboardFormFactor.MicroATX, MotherboardFormFactor.MiniITX], MaxGpuLengthMm = 335 });
        var case3 = new Product(cat.Id, "Lian Li Lancool III", $"case-lancool3-{Guid.NewGuid():N}", $"SKU-CASE-3-{Guid.NewGuid():N}", "lian li", 3900000m, 6, "https://example.com/cs3.jpg", ComponentType.Case,
            new CaseSpecification { SupportedFormFactors = [MotherboardFormFactor.MiniITX, MotherboardFormFactor.EATX], MaxGpuLengthMm = 420 });
        productsByType[ComponentType.Case] = [case1, case2, case3];

        // 8. Cooler
        var cooler1 = new Product(cat.Id, "DeepCool LT720", $"cooler-lt720-{Guid.NewGuid():N}", $"SKU-COOL-1-{Guid.NewGuid():N}", "DeepCool", 3200000m, 8, "https://example.com/cl1.jpg", ComponentType.Cooler,
            new CoolerSpecification { CoolerType = CoolerType.AIO_360, SupportedSockets = [CpuSocket.AM5, CpuSocket.LGA1700] });
        var cooler2 = new Product(cat.Id, "Noctua NH-U12A", $"cooler-u12a-{Guid.NewGuid():N}", $"SKU-COOL-2-{Guid.NewGuid():N}", "Noctua", 2800000m, 5, "https://example.com/cl2.jpg", ComponentType.Cooler,
            new CoolerSpecification { CoolerType = CoolerType.Air, SupportedSockets = [CpuSocket.LGA1700, CpuSocket.AM4] });
        var cooler3 = new Product(cat.Id, "DeepCool LS520", $"cooler-ls520-{Guid.NewGuid():N}", $"SKU-COOL-3-{Guid.NewGuid():N}", "deepcool", 2400000m, 6, "https://example.com/cl3.jpg", ComponentType.Cooler,
            new CoolerSpecification { CoolerType = CoolerType.AIO_240, SupportedSockets = [CpuSocket.AM4, CpuSocket.LGA1851] });
        productsByType[ComponentType.Cooler] = [cooler1, cooler2, cooler3];

        // 9. Accessory
        var acc1 = new Product(cat.Id, "Logitech G Pro X Superlight", $"acc-gprox-{Guid.NewGuid():N}", $"SKU-ACC-1-{Guid.NewGuid():N}", "Logitech", 2900000m, 10, "https://example.com/a1.jpg", ComponentType.Accessory,
            new AccessorySpecification());
        var acc2 = new Product(cat.Id, "Logitech G915 TKL", $"acc-g915-{Guid.NewGuid():N}", $"SKU-ACC-2-{Guid.NewGuid():N}", "logitech", 4200000m, 4, "https://example.com/a2.jpg", ComponentType.Accessory,
            new AccessorySpecification());
        var acc3 = new Product(cat.Id, "Razer BlackShark V2 Pro", $"acc-blackshark-{Guid.NewGuid():N}", $"SKU-ACC-3-{Guid.NewGuid():N}", "Razer", 3500000m, 7, "https://example.com/a3.jpg", ComponentType.Accessory,
            new AccessorySpecification());
        productsByType[ComponentType.Accessory] = [acc1, acc2, acc3];

        var allProducts = productsByType.Values.SelectMany(p => p).ToList();
        db.Products.AddRange(allProducts);
        await db.SaveChangesAsync();

        return new NineSubtypesCatalog(cat, productsByType);
    }

    [Fact]
    public async Task GetFilterOptions_AllNineSubtypes_ExactKeysAndValues_SortedDistinct()
    {
        var catalog = await SeedNineSubtypesCatalogAsync();
        using var client = CreatePublicClient();

        // 1. CPU
        var cpuResp = await client.GetAsync($"/api/v1/products/filter-options?categoryId={catalog.RootCat.Id}&componentType=1");
        cpuResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var cpuOpt = (await cpuResp.Content.ReadFromJsonAsync<ProductFilterOptionsDto>(JsonOptions))!;
        cpuOpt.Brands.Should().Equal(["AMD", "Intel"]);
        cpuOpt.Groups.Select(g => g.Key).Should().Equal(["sockets", "memoryTypes", "coreCounts"]);
        cpuOpt.Groups.Single(g => g.Key == "sockets").Values.Should().Equal([1, 4, 5]);
        cpuOpt.Groups.Single(g => g.Key == "memoryTypes").Values.Should().Equal([1, 2]);
        cpuOpt.Groups.Single(g => g.Key == "coreCounts").Values.Should().Equal([6, 8, 14]);

        // 2. Motherboard
        var mbResp = await client.GetAsync($"/api/v1/products/filter-options?categoryId={catalog.RootCat.Id}&componentType=2");
        mbResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var mbOpt = (await mbResp.Content.ReadFromJsonAsync<ProductFilterOptionsDto>(JsonOptions))!;
        mbOpt.Brands.Should().Equal(["ASUS", "MSI"]);
        mbOpt.Groups.Select(g => g.Key).Should().Equal(["sockets", "memoryTypes", "motherboardFormFactors"]);
        mbOpt.Groups.Single(g => g.Key == "sockets").Values.Should().Equal([1, 4, 5]);
        mbOpt.Groups.Single(g => g.Key == "memoryTypes").Values.Should().Equal([1, 2]);
        mbOpt.Groups.Single(g => g.Key == "motherboardFormFactors").Values.Should().Equal([1, 2, 3]);

        // 3. GPU
        var gpuResp = await client.GetAsync($"/api/v1/products/filter-options?categoryId={catalog.RootCat.Id}&componentType=3");
        gpuResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var gpuOpt = (await gpuResp.Content.ReadFromJsonAsync<ProductFilterOptionsDto>(JsonOptions))!;
        gpuOpt.Brands.Should().Equal(["Gigabyte", "ZOTAC"]);
        gpuOpt.Groups.Select(g => g.Key).Should().Equal(["vramGbs", "gpuLengthsMm"]);
        gpuOpt.Groups.Single(g => g.Key == "vramGbs").Values.Should().Equal([12, 16, 24]);
        gpuOpt.Groups.Single(g => g.Key == "gpuLengthsMm").Values.Should().Equal([267, 304, 320]);

        // 4. RAM
        var ramResp = await client.GetAsync($"/api/v1/products/filter-options?categoryId={catalog.RootCat.Id}&componentType=4");
        ramResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var ramOpt = (await ramResp.Content.ReadFromJsonAsync<ProductFilterOptionsDto>(JsonOptions))!;
        ramOpt.Brands.Should().Equal(["Corsair", "Kingston"]);
        ramOpt.Groups.Select(g => g.Key).Should().Equal(["memoryTypes", "ramFormFactors", "ramCapacitiesGb", "ramSpeedsMhz"]);
        ramOpt.Groups.Single(g => g.Key == "memoryTypes").Values.Should().Equal([1, 2]);
        ramOpt.Groups.Single(g => g.Key == "ramFormFactors").Values.Should().Equal([1, 2]);
        ramOpt.Groups.Single(g => g.Key == "ramCapacitiesGb").Values.Should().Equal([16, 32, 64]);
        ramOpt.Groups.Single(g => g.Key == "ramSpeedsMhz").Values.Should().Equal([3200, 5600, 6000]);

        // 5. Storage
        var stResp = await client.GetAsync($"/api/v1/products/filter-options?categoryId={catalog.RootCat.Id}&componentType=5");
        stResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var stOpt = (await stResp.Content.ReadFromJsonAsync<ProductFilterOptionsDto>(JsonOptions))!;
        stOpt.Brands.Should().Equal(["Crucial", "Samsung"]);
        stOpt.Groups.Select(g => g.Key).Should().Equal(["storageTypes", "storageInterfaces", "storageCapacitiesGb"]);
        stOpt.Groups.Single(g => g.Key == "storageTypes").Values.Should().Equal([1, 2]);
        stOpt.Groups.Single(g => g.Key == "storageInterfaces").Values.Should().Equal([2, 3, 4]);
        stOpt.Groups.Single(g => g.Key == "storageCapacitiesGb").Values.Should().Equal([500, 1000, 2000]);

        // 6. PSU
        var psuResp = await client.GetAsync($"/api/v1/products/filter-options?categoryId={catalog.RootCat.Id}&componentType=6");
        psuResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var psuOpt = (await psuResp.Content.ReadFromJsonAsync<ProductFilterOptionsDto>(JsonOptions))!;
        psuOpt.Brands.Should().Equal(["Corsair", "Seasonic"]);
        psuOpt.Groups.Select(g => g.Key).Should().Equal(["psuEfficiencies", "psuModularities", "psuFormFactors", "psuWattages"]);
        psuOpt.Groups.Single(g => g.Key == "psuEfficiencies").Values.Should().Equal([2, 4, 5]);
        psuOpt.Groups.Single(g => g.Key == "psuModularities").Values.Should().Equal([2, 3]);
        psuOpt.Groups.Single(g => g.Key == "psuFormFactors").Values.Should().Equal([1, 2]);
        psuOpt.Groups.Single(g => g.Key == "psuWattages").Values.Should().Equal([650, 850, 1000]);

        // 7. Case
        var caseResp = await client.GetAsync($"/api/v1/products/filter-options?categoryId={catalog.RootCat.Id}&componentType=7");
        caseResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var caseOpt = (await caseResp.Content.ReadFromJsonAsync<ProductFilterOptionsDto>(JsonOptions))!;
        caseOpt.Brands.Should().Equal(["Fractal", "Lian Li"]);
        caseOpt.Groups.Select(g => g.Key).Should().Equal(["motherboardFormFactors"]);
        caseOpt.Groups.Single(g => g.Key == "motherboardFormFactors").Values.Should().Equal([1, 2, 3, 4]);

        // 8. Cooler
        var coolerResp = await client.GetAsync($"/api/v1/products/filter-options?categoryId={catalog.RootCat.Id}&componentType=8");
        coolerResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var coolerOpt = (await coolerResp.Content.ReadFromJsonAsync<ProductFilterOptionsDto>(JsonOptions))!;
        coolerOpt.Brands.Should().Equal(["DeepCool", "Noctua"]);
        coolerOpt.Groups.Select(g => g.Key).Should().Equal(["sockets", "coolerTypes"]);
        coolerOpt.Groups.Single(g => g.Key == "sockets").Values.Should().Equal([1, 2, 4, 5]);
        coolerOpt.Groups.Single(g => g.Key == "coolerTypes").Values.Should().Equal([1, 3, 5]);

        // 9. Accessory
        var accResp = await client.GetAsync($"/api/v1/products/filter-options?categoryId={catalog.RootCat.Id}&componentType=9");
        accResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var accOpt = (await accResp.Content.ReadFromJsonAsync<ProductFilterOptionsDto>(JsonOptions))!;
        accOpt.Brands.Should().Equal(["Logitech", "Razer"]);
        accOpt.Groups.Should().BeEmpty();

        // 10. Scope without componentType -> returns all brands across category, groups empty
        var allBrandsResp = await client.GetAsync($"/api/v1/products/filter-options?categoryId={catalog.RootCat.Id}");
        allBrandsResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var allBrandsOpt = (await allBrandsResp.Content.ReadFromJsonAsync<ProductFilterOptionsDto>(JsonOptions))!;
        allBrandsOpt.Brands.Should().Equal([
            "AMD", "ASUS", "Corsair", "Crucial", "DeepCool", "Fractal", "Gigabyte", "Intel", "Kingston", "Lian Li", "Logitech", "MSI", "Noctua", "Razer", "Samsung", "Seasonic", "ZOTAC"
        ]);
        allBrandsOpt.Groups.Should().BeEmpty();

        // 11. Invalid componentType -> 400 Bad Request
        var invalidTypeResp = await client.GetAsync($"/api/v1/products/filter-options?categoryId={catalog.RootCat.Id}&componentType=99");
        invalidTypeResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 12. Empty scope -> 200 OK with empty values
        using var emptyScope = Fixture.Factory.Services.CreateScope();
        var emptyDb = emptyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var emptyCat = new Category("Empty Cat", $"empty-{Guid.NewGuid():N}", ComponentType.Cpu);
        emptyDb.Categories.Add(emptyCat);
        await emptyDb.SaveChangesAsync();

        var emptyResp = await client.GetAsync($"/api/v1/products/filter-options?categoryId={emptyCat.Id}&componentType=1");
        emptyResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var emptyOpt = (await emptyResp.Content.ReadFromJsonAsync<ProductFilterOptionsDto>(JsonOptions))!;
        emptyOpt.Brands.Should().BeEmpty();
        emptyOpt.Groups.Should().HaveCount(3);
        emptyOpt.Groups.All(g => g.Values.Count == 0).Should().BeTrue();
    }

    [Fact]
    public async Task GetFilterOptions_AllGroupsAndValues_RoundtripToProductsQuery_MatchesExactProductIds()
    {
        var catalog = await SeedNineSubtypesCatalogAsync();
        using var client = CreatePublicClient();

        var types = new[]
        {
            ComponentType.Cpu,
            ComponentType.Motherboard,
            ComponentType.Gpu,
            ComponentType.Ram,
            ComponentType.Storage,
            ComponentType.Psu,
            ComponentType.Case,
            ComponentType.Cooler
        };

        foreach (var type in types)
        {
            var optResp = await client.GetAsync($"/api/v1/products/filter-options?categoryId={catalog.RootCat.Id}&componentType={(int)type}");
            optResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var opt = (await optResp.Content.ReadFromJsonAsync<ProductFilterOptionsDto>(JsonOptions))!;

            // Test roundtrip for every group and value
            foreach (var group in opt.Groups)
            {
                foreach (var val in group.Values)
                {
                    var prodResp = await client.GetAsync($"/api/v1/products?categoryId={catalog.RootCat.Id}&componentType={(int)type}&{group.Key}={val}");
                    prodResp.StatusCode.Should().Be(HttpStatusCode.OK);
                    var prodResult = (await prodResp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
                    prodResult.TotalCount.Should().BeGreaterThan(0);
                    prodResult.Items.Should().NotBeEmpty();
                }
            }

            // Test roundtrip for every brand
            foreach (var brand in opt.Brands)
            {
                var prodResp = await client.GetAsync($"/api/v1/products?categoryId={catalog.RootCat.Id}&componentType={(int)type}&brands={Uri.EscapeDataString(brand)}");
                prodResp.StatusCode.Should().Be(HttpStatusCode.OK);
                var prodResult = (await prodResp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
                prodResult.TotalCount.Should().BeGreaterThan(0);
                prodResult.Items.All(p => p.Brand.Trim().Equals(brand.Trim(), StringComparison.OrdinalIgnoreCase)).Should().BeTrue();
            }
        }

        // Multi-choice OR queries for scalar group and array group
        // 1. CPU scalar: sockets=5&sockets=1 -> matches cpu1 (AM5) and cpu3 (LGA1700)
        var cpuMultiResp = await client.GetAsync($"/api/v1/products?categoryId={catalog.RootCat.Id}&componentType=1&sockets=5&sockets=1");
        cpuMultiResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var cpuMultiResult = (await cpuMultiResp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        cpuMultiResult.Items.Select(p => p.Id).Should().BeEquivalentTo([
            catalog.ProductsByType[ComponentType.Cpu][0].Id,
            catalog.ProductsByType[ComponentType.Cpu][2].Id
        ]);

        // 2. Cooler array: sockets=5&sockets=2 -> matches cooler1 (supports AM5) and cooler3 (supports LGA1851)
        var coolerMultiResp = await client.GetAsync($"/api/v1/products?categoryId={catalog.RootCat.Id}&componentType=8&sockets=5&sockets=2");
        coolerMultiResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var coolerMultiResult = (await coolerMultiResp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        coolerMultiResult.Items.Select(p => p.Id).Should().BeEquivalentTo([
            catalog.ProductsByType[ComponentType.Cooler][0].Id,
            catalog.ProductsByType[ComponentType.Cooler][2].Id
        ]);
    }

    [Fact]
    public async Task GetFilterOptions_ResilientToLegacyCorruptedOrNullSpecs_DoesNotEmitInvalidOptions()
    {
        using var scope = Fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var cat = new Category("Corrupted Specs Test", $"corrupt-cat-{Guid.NewGuid():N}", ComponentType.Cpu);
        db.Categories.Add(cat);
        await db.SaveChangesAsync();

        var validCpu = new Product(cat.Id, "Valid CPU", $"valid-cpu-{Guid.NewGuid():N}", $"SKU-VALID-{Guid.NewGuid():N}", "AMD", 5000000m, 10, "https://example.com/v.jpg", ComponentType.Cpu,
            new CpuSpecification { Socket = CpuSocket.AM5, SupportedMemoryType = RamType.DDR5, CoreCount = 6 });
        db.Products.Add(validCpu);
        await db.SaveChangesAsync();

        var corruptId = Guid.NewGuid();
        var corruptJson = "{\"Socket\": 9999, \"CoreCount\": -10, \"SupportedMemoryType\": \"non_numeric\"}";
        var corruptSql = """
            INSERT INTO "Products" ("Id", "CategoryId", "Name", "Slug", "Sku", "Brand", "Price", "StockQuantity", "IsActive", "ImageUrl", "AdditionalImages", "ComponentType", "Specifications", "CreatedAt")
            VALUES (@p0, @p1, 'Corrupted CPU', @p2, @p3, 'CorruptBrand', 1000, 10, true, 'https://example.com/c.jpg', ARRAY[]::text[], 1, @p4::jsonb, NOW());
        """;
        await db.Database.ExecuteSqlRawAsync(corruptSql, corruptId, cat.Id, $"corrupt-{corruptId:N}", $"SKU-{corruptId:N}", corruptJson);

        using var client = CreatePublicClient();
        var resp = await client.GetAsync($"/api/v1/products/filter-options?categoryId={cat.Id}&componentType=1");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var options = (await resp.Content.ReadFromJsonAsync<ProductFilterOptionsDto>(JsonOptions))!;
        var socketsGroup = options.Groups.Single(g => g.Key == "sockets");
        socketsGroup.Values.Should().BeEquivalentTo([5]); // Socket 9999 is invalid enum, excluded
        var coresGroup = options.Groups.Single(g => g.Key == "coreCounts");
        coresGroup.Values.Should().BeEquivalentTo([6]); // CoreCount -10 is non-positive, excluded
        var memoryGroup = options.Groups.Single(g => g.Key == "memoryTypes");
        memoryGroup.Values.Should().BeEquivalentTo([2]); // Non-numeric is excluded

        // Sending options back to products does not return 400
        var prodResp = await client.GetAsync($"/api/v1/products?categoryId={cat.Id}&componentType=1&sockets=5&coreCounts=6");
        prodResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetFilterOptions_CategoryHierarchy_ThreeLevelsAndOutsideBranch_ActiveStockCombinations()
    {
        using var scope = Fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var rootCat = new Category("Hierarchy Root", $"root-{Guid.NewGuid():N}", ComponentType.Cpu);
        db.Categories.Add(rootCat);
        await db.SaveChangesAsync();

        var childCat = new Category("Hierarchy Child", $"child-{Guid.NewGuid():N}", ComponentType.Cpu, parentId: rootCat.Id);
        db.Categories.Add(childCat);
        await db.SaveChangesAsync();

        var grandCat = new Category("Hierarchy Grandchild", $"grand-{Guid.NewGuid():N}", ComponentType.Cpu, parentId: childCat.Id);
        var outsideCat = new Category("Hierarchy Outside", $"outside-{Guid.NewGuid():N}", ComponentType.Cpu);
        db.Categories.AddRange(grandCat, outsideCat);
        await db.SaveChangesAsync();

        // Root product: Brand "RootBrand", Active, InStock, 8 cores
        var pRoot = new Product(rootCat.Id, "Root CPU", $"p-root-{Guid.NewGuid():N}", $"SKU-R-{Guid.NewGuid():N}", "RootBrand", 1000m, 10, "r.jpg", ComponentType.Cpu,
            new CpuSpecification { Socket = CpuSocket.AM5, CoreCount = 8 });

        // Child product: Brand "ChildBrand", Inactive, InStock, 12 cores
        var pChild = new Product(childCat.Id, "Child CPU", $"p-child-{Guid.NewGuid():N}", $"SKU-C-{Guid.NewGuid():N}", "ChildBrand", 2000m, 5, "c.jpg", ComponentType.Cpu,
            new CpuSpecification { Socket = CpuSocket.AM5, CoreCount = 12 }) { IsActive = false };

        // Grandchild product: Brand "GrandBrand", Active, OutOfStock, 16 cores
        var pGrand = new Product(grandCat.Id, "Grand CPU", $"p-grand-{Guid.NewGuid():N}", $"SKU-G-{Guid.NewGuid():N}", "GrandBrand", 3000m, 0, "g.jpg", ComponentType.Cpu,
            new CpuSpecification { Socket = CpuSocket.LGA1700, CoreCount = 16 });

        // Outside product: Brand "OutsideBrand", Active, InStock, 24 cores
        var pOutside = new Product(outsideCat.Id, "Outside CPU", $"p-outside-{Guid.NewGuid():N}", $"SKU-O-{Guid.NewGuid():N}", "OutsideBrand", 4000m, 8, "o.jpg", ComponentType.Cpu,
            new CpuSpecification { Socket = CpuSocket.TR4, CoreCount = 24 });

        db.Products.AddRange(pRoot, pChild, pGrand, pOutside);
        await db.SaveChangesAsync();

        using var client = CreatePublicClient();

        // 1. Root includes Root, Child, Grandchild; excludes Outside
        var respRoot = await client.GetAsync($"/api/v1/products/filter-options?categoryId={rootCat.Id}&componentType=1");
        respRoot.StatusCode.Should().Be(HttpStatusCode.OK);
        var optRoot = (await respRoot.Content.ReadFromJsonAsync<ProductFilterOptionsDto>(JsonOptions))!;
        optRoot.Brands.Should().BeEquivalentTo(["ChildBrand", "GrandBrand", "RootBrand"]);
        optRoot.Groups.Single(g => g.Key == "coreCounts").Values.Should().BeEquivalentTo([8, 12, 16]);

        // 2. Child includes Child, Grandchild; excludes Root and Outside
        var respChild = await client.GetAsync($"/api/v1/products/filter-options?categoryId={childCat.Id}&componentType=1");
        respChild.StatusCode.Should().Be(HttpStatusCode.OK);
        var optChild = (await respChild.Content.ReadFromJsonAsync<ProductFilterOptionsDto>(JsonOptions))!;
        optChild.Brands.Should().BeEquivalentTo(["ChildBrand", "GrandBrand"]);
        optChild.Groups.Single(g => g.Key == "coreCounts").Values.Should().BeEquivalentTo([12, 16]);

        // 3. Grandchild includes Grandchild only
        var respGrand = await client.GetAsync($"/api/v1/products/filter-options?categoryId={grandCat.Id}&componentType=1");
        respGrand.StatusCode.Should().Be(HttpStatusCode.OK);
        var optGrand = (await respGrand.Content.ReadFromJsonAsync<ProductFilterOptionsDto>(JsonOptions))!;
        optGrand.Brands.Should().BeEquivalentTo(["GrandBrand"]);
        optGrand.Groups.Single(g => g.Key == "coreCounts").Values.Should().BeEquivalentTo([16]);

        // 4. Scoped isActive=true on RootCat -> excludes Child (inactive)
        var respActive = await client.GetAsync($"/api/v1/products/filter-options?categoryId={rootCat.Id}&componentType=1&isActive=true");
        respActive.StatusCode.Should().Be(HttpStatusCode.OK);
        var optActive = (await respActive.Content.ReadFromJsonAsync<ProductFilterOptionsDto>(JsonOptions))!;
        optActive.Brands.Should().BeEquivalentTo(["GrandBrand", "RootBrand"]);
        optActive.Groups.Single(g => g.Key == "coreCounts").Values.Should().BeEquivalentTo([8, 16]);

        // 5. Scoped inStock=true on RootCat -> excludes Grand (out of stock)
        var respStock = await client.GetAsync($"/api/v1/products/filter-options?categoryId={rootCat.Id}&componentType=1&inStock=true");
        respStock.StatusCode.Should().Be(HttpStatusCode.OK);
        var optStock = (await respStock.Content.ReadFromJsonAsync<ProductFilterOptionsDto>(JsonOptions))!;
        optStock.Brands.Should().BeEquivalentTo(["ChildBrand", "RootBrand"]);
        optStock.Groups.Single(g => g.Key == "coreCounts").Values.Should().BeEquivalentTo([8, 12]);
    }

    [Fact]
    public async Task GetFilterOptions_DatasetGreaterThanPageSize_EndDatasetValuesIncluded_IndependentOfSearchAndPrice()
    {
        using var scope = Fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var cat = new Category("Large Dataset Test", $"large-cat-{Guid.NewGuid():N}", ComponentType.Gpu);
        db.Categories.Add(cat);
        await db.SaveChangesAsync();

        var products = new List<Product>();
        for (int i = 1; i <= 24; i++)
        {
            products.Add(new Product(
                cat.Id,
                $"GPU #{i}",
                $"gpu-large-{i}-{Guid.NewGuid():N}",
                $"SKU-LG-{i}-{Guid.NewGuid():N}",
                "CommonBrand",
                1000000m + (i * 10000m),
                10,
                $"g{i}.jpg",
                ComponentType.Gpu,
                new GpuSpecification { VramGb = 8, LengthMm = 200 }));
        }

        // 25th product at the end of the dataset
        var endProduct = new Product(
            cat.Id,
            "GPU #25 End",
            $"gpu-large-25-{Guid.NewGuid():N}",
            $"SKU-LG-25-{Guid.NewGuid():N}",
            "RareBrand",
            9000000m,
            10,
            "g25.jpg",
            ComponentType.Gpu,
            new GpuSpecification { VramGb = 48, LengthMm = 350 });
        products.Add(endProduct);

        db.Products.AddRange(products);
        await db.SaveChangesAsync();

        using var client = CreatePublicClient();

        // filter-options includes the values from the product at the end of dataset
        var optResp = await client.GetAsync($"/api/v1/products/filter-options?categoryId={cat.Id}&componentType=3");
        optResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var opt = (await optResp.Content.ReadFromJsonAsync<ProductFilterOptionsDto>(JsonOptions))!;
        opt.Brands.Should().Contain("RareBrand");
        opt.Groups.Single(g => g.Key == "vramGbs").Values.Should().Contain(48);
        opt.Groups.Single(g => g.Key == "gpuLengthsMm").Values.Should().Contain(350);

        // GET products with search and price restrictions returns page 1
        var listResp = await client.GetAsync($"/api/v1/products?categoryId={cat.Id}&componentType=3&searchTerm=CommonBrand&minPrice=1000000&maxPrice=1100000&pageIndex=1&pageSize=5");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var listResult = (await listResp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        listResult.Items.Should().NotContain(p => p.Id == endProduct.Id);

        // filter-options remains unchanged by product query parameters
        var optResp2 = await client.GetAsync($"/api/v1/products/filter-options?categoryId={cat.Id}&componentType=3");
        optResp2.StatusCode.Should().Be(HttpStatusCode.OK);
        var opt2 = (await optResp2.Content.ReadFromJsonAsync<ProductFilterOptionsDto>(JsonOptions))!;
        opt2.Brands.Should().Contain("RareBrand");
        opt2.Groups.Single(g => g.Key == "vramGbs").Values.Should().Contain(48);
    }

    [Fact]
    public async Task GetFilterOptions_DeterministicBrandCasing_IndependentOfInsertionOrder()
    {
        using var scope = Fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var catA = new Category("Cat A Casing", $"cat-a-{Guid.NewGuid():N}", ComponentType.Cpu);
        var catB = new Category("Cat B Casing", $"cat-b-{Guid.NewGuid():N}", ComponentType.Cpu);
        db.Categories.AddRange(catA, catB);
        await db.SaveChangesAsync();

        // In Cat A: insert lowercase/spaces first, then PascalCase
        var pA1 = new Product(catA.Id, "PA1", $"pa1-{Guid.NewGuid():N}", $"SKU-PA1-{Guid.NewGuid():N}", "  intel  ", 1000m, 10, "a1.jpg", ComponentType.Cpu, new CpuSpecification { Socket = CpuSocket.LGA1700 });
        db.Products.Add(pA1);
        await db.SaveChangesAsync();

        var pA2 = new Product(catA.Id, "PA2", $"pa2-{Guid.NewGuid():N}", $"SKU-PA2-{Guid.NewGuid():N}", "Intel", 2000m, 10, "a2.jpg", ComponentType.Cpu, new CpuSpecification { Socket = CpuSocket.LGA1700 });
        db.Products.Add(pA2);
        await db.SaveChangesAsync();

        // In Cat B: insert PascalCase first, then lowercase/spaces
        var pB1 = new Product(catB.Id, "PB1", $"pb1-{Guid.NewGuid():N}", $"SKU-PB1-{Guid.NewGuid():N}", "Intel", 2000m, 10, "b1.jpg", ComponentType.Cpu, new CpuSpecification { Socket = CpuSocket.LGA1700 });
        db.Products.Add(pB1);
        await db.SaveChangesAsync();

        var pB2 = new Product(catB.Id, "PB2", $"pb2-{Guid.NewGuid():N}", $"SKU-PB2-{Guid.NewGuid():N}", "  intel  ", 1000m, 10, "b2.jpg", ComponentType.Cpu, new CpuSpecification { Socket = CpuSocket.LGA1700 });
        db.Products.Add(pB2);
        await db.SaveChangesAsync();

        using var client = CreatePublicClient();

        var respA = await client.GetAsync($"/api/v1/products/filter-options?categoryId={catA.Id}");
        respA.StatusCode.Should().Be(HttpStatusCode.OK);
        var optA = (await respA.Content.ReadFromJsonAsync<ProductFilterOptionsDto>(JsonOptions))!;

        var respB = await client.GetAsync($"/api/v1/products/filter-options?categoryId={catB.Id}");
        respB.StatusCode.Should().Be(HttpStatusCode.OK);
        var optB = (await respB.Content.ReadFromJsonAsync<ProductFilterOptionsDto>(JsonOptions))!;

        // Both must return the deterministic MIN representative "Intel", not varying based on insertion order
        optA.Brands.Should().Equal(["Intel"]);
        optB.Brands.Should().Equal(["Intel"]);
    }

    [Fact]
    public async Task GetFilterOptions_QueryInstrumentation_ProvesPostgreSqlAggregationAndBoundedQueries()
    {
        using var scope = Fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var catSmall = new Category("Small Dataset", $"sm-{Guid.NewGuid():N}", ComponentType.Cpu);
        var catLarge = new Category("Large Dataset", $"lg-{Guid.NewGuid():N}", ComponentType.Cpu);
        db.Categories.AddRange(catSmall, catLarge);
        await db.SaveChangesAsync();

        // Small dataset: 2 products
        var pSm1 = new Product(catSmall.Id, "Sm1", $"sm1-{Guid.NewGuid():N}", $"SKU-SM1-{Guid.NewGuid():N}", "AMD", 1000m, 10, "s1.jpg", ComponentType.Cpu,
            new CpuSpecification { Socket = CpuSocket.AM5, SupportedMemoryType = RamType.DDR5, CoreCount = 6 });
        var pSm2 = new Product(catSmall.Id, "Sm2", $"sm2-{Guid.NewGuid():N}", $"SKU-SM2-{Guid.NewGuid():N}", "Intel", 2000m, 10, "s2.jpg", ComponentType.Cpu,
            new CpuSpecification { Socket = CpuSocket.LGA1700, SupportedMemoryType = RamType.DDR4, CoreCount = 8 });
        db.Products.AddRange(pSm1, pSm2);

        // Large dataset: 20 products with same distinct values
        var lgProducts = new List<Product>();
        for (int i = 1; i <= 20; i++)
        {
            var brand = i % 2 == 0 ? "AMD" : "Intel";
            var socket = i % 2 == 0 ? CpuSocket.AM5 : CpuSocket.LGA1700;
            var mem = i % 2 == 0 ? RamType.DDR5 : RamType.DDR4;
            var cores = i % 2 == 0 ? 6 : 8;
            lgProducts.Add(new Product(catLarge.Id, $"Lg{i}", $"lg{i}-{Guid.NewGuid():N}", $"SKU-LG-{i}-{Guid.NewGuid():N}", brand, 1000m, 10, $"l{i}.jpg", ComponentType.Cpu,
                new CpuSpecification { Socket = socket, SupportedMemoryType = mem, CoreCount = cores }));
        }
        db.Products.AddRange(lgProducts);
        await db.SaveChangesAsync();

        var interceptor = new TestQueryCaptureInterceptor();
        using var customFactory = Fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.First(d => d.ServiceType == typeof(IProductSpecificationFilterService));
                services.Remove(descriptor);

                services.AddScoped<IProductSpecificationFilterService>(sp =>
                {
                    var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
                    optionsBuilder.UseNpgsql(Fixture.ConnectionString, npgsqlOptions =>
                    {
                        npgsqlOptions.UseVector();
                        npgsqlOptions.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
                    });
                    optionsBuilder.AddInterceptors(interceptor);
                    var dbInstance = new ApplicationDbContext(optionsBuilder.Options);
                    return new ProductSpecificationFilterService(dbInstance);
                });
            });
        });
        using var client = customFactory.CreateClient();

        // 1. Query small dataset
        interceptor.Commands.Clear();
        var respSmall = await client.GetAsync($"/api/v1/products/filter-options?categoryId={catSmall.Id}&componentType=1");
        respSmall.StatusCode.Should().Be(HttpStatusCode.OK);
        var optSmall = (await respSmall.Content.ReadFromJsonAsync<ProductFilterOptionsDto>(JsonOptions))!;

        var smallCommands = interceptor.Commands.Where(c => c.Contains("\"Products\"", StringComparison.OrdinalIgnoreCase)).ToList();
        smallCommands.Should().HaveCount(2, "1 query for brands, 1 query for specification groups");

        // Command 1: Brands query
        var brandsCmd = smallCommands[0];
        brandsCmd.Should().Contain("GROUP BY");
        brandsCmd.Should().Contain("TRIM");
        brandsCmd.Should().Contain("UPPER");
        brandsCmd.Should().NotContain("\"Specifications\"", "Brands query must not project or touch Specifications column");

        // Command 2: Groups query
        var groupsCmd = smallCommands[1];
        groupsCmd.Should().Contain("UNION");
        groupsCmd.Should().Contain("->>");
        groupsCmd.Should().Contain("scoped_products");
        groupsCmd.Should().NotContain("SELECT p.*", "Groups query must only project Key and Value, not whole product entities");

        // 2. Query large dataset
        interceptor.Commands.Clear();
        var respLarge = await client.GetAsync($"/api/v1/products/filter-options?categoryId={catLarge.Id}&componentType=1");
        respLarge.StatusCode.Should().Be(HttpStatusCode.OK);
        var optLarge = (await respLarge.Content.ReadFromJsonAsync<ProductFilterOptionsDto>(JsonOptions))!;

        var largeCommands = interceptor.Commands.Where(c => c.Contains("\"Products\"", StringComparison.OrdinalIgnoreCase)).ToList();
        largeCommands.Should().HaveCount(2, "Query count must remain bounded O(1) regardless of dataset size");

        // 3. Values must be identical between small and large dataset
        optSmall.Brands.Should().BeEquivalentTo(optLarge.Brands);
        optSmall.Groups.Should().BeEquivalentTo(optLarge.Groups);
    }
}

using FluentAssertions;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Features.Ai.Queries;
using RiuTek.Application.Services;
using RiuTek.Application.Test.Helpers;
using RiuTek.Core.Entities;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;
using RiuTek.Infrastructure.Services;

namespace RiuTek.Application.Test.Features.Ai;

public class RecommendAiBuildTests
{
    [Fact]
    public async Task GamingBuild_ReusesOwnedCpuAndSkipsIncompatibleAndUnavailableParts()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        await SeedCatalog(context);
        var plan = new GroqStructuredPlan("recommend_build", [], MaxPrice: 7_000_000,
            BuildUseCase: "gaming", OwnedProducts: ["CPU-OWNED"]);

        var reply = await new RecommendAiBuildQueryHandler(context, new HardwareCompatibilityService())
            .Handle(new RecommendAiBuildQuery(plan), CancellationToken.None);

        reply.Status.Should().Be("answered");
        var build = reply.Build!;
        build.Items.Select(i => i.Product.Sku).Should().Equal(
            "CPU-OWNED", "BOARD-AM5", "RAM-16", "SSD", "GPU-FITS", "PSU", "CASE", "COOLER");
        build.Items.Should().ContainSingle(i => i.AlreadyOwned && i.Product.Sku == "CPU-OWNED");
        build.TotalVnd.Should().Be(7_000_000);
        build.Items.Where(i => !i.AlreadyOwned).Sum(i => i.Product.PriceVnd).Should().Be(build.TotalVnd);
        build.RecommendedPsuWattage.Should().Be(550);
    }

    [Fact]
    public async Task OfficeBuild_UsesIntegratedGraphicsInsteadOfBuyingGpu()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        await SeedCatalog(context);
        var plan = new GroqStructuredPlan("recommend_build", [], MaxPrice: 8_000_000, BuildUseCase: "office");

        var reply = await new RecommendAiBuildQueryHandler(context, new HardwareCompatibilityService())
            .Handle(new RecommendAiBuildQuery(plan), CancellationToken.None);

        reply.Status.Should().Be("answered");
        reply.Build!.Items.Select(i => i.Product.Sku).Should().Contain("CPU-OFFICE");
        reply.Build.Items.Should().HaveCount(7).And.NotContain(i => i.Product.Sku.StartsWith("GPU-"));
    }

    [Fact]
    public async Task GamingBuild_WhenBudgetOrCapacityCannotBeMet_ReturnsNoBuild()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        await SeedCatalog(context);
        var handler = new RecommendAiBuildQueryHandler(context, new HardwareCompatibilityService());
        var plan = new GroqStructuredPlan("recommend_build", [], MaxPrice: 6_999_999,
            BuildUseCase: "gaming", OwnedProducts: ["CPU-OWNED"]);

        var insufficientBudget = await handler.Handle(new RecommendAiBuildQuery(plan), CancellationToken.None);
        var insufficientRam = await handler.Handle(new RecommendAiBuildQuery(plan with
        {
            MaxPrice = 20_000_000,
            TechnicalCriteria = new Dictionary<string, string> { ["minRamGb"] = "32" }
        }), CancellationToken.None);

        insufficientBudget.Status.Should().Be("insufficient_data");
        insufficientBudget.Build.Should().BeNull();
        insufficientRam.Status.Should().Be("insufficient_data");
        insufficientRam.Build.Should().BeNull();
    }

    [Fact]
    public async Task RequiredUnidentifiedPart_AsksForModelRatherThanReplacingIt()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var plan = new GroqStructuredPlan("recommend_build", [], MaxPrice: 20_000_000,
            BuildUseCase: "gaming", OwnedProducts: ["external GPU"]);

        var reply = await new RecommendAiBuildQueryHandler(context, new HardwareCompatibilityService())
            .Handle(new RecommendAiBuildQuery(plan), CancellationToken.None);

        reply.Status.Should().Be("needs_clarification");
        reply.Build.Should().BeNull();
    }

    [Fact]
    public void GroqPlanParser_ReadsBuildIntentAndOwnedProducts()
    {
        var result = GroqClient.ParseStructuredPlanJson("""
            {"task_type":"recommend_build","build_use_case":"gaming","max_price":20000000,
             "target_products":["GPU-FITS"],"owned_products":["CPU-OWNED"]}
            """);

        result.IsSuccess.Should().BeTrue();
        result.Value.BuildUseCase.Should().Be("gaming");
        result.Value.MaxPrice.Should().Be(20_000_000);
        result.Value.TargetProducts.Should().Equal("GPU-FITS");
        result.Value.OwnedProducts.Should().Equal("CPU-OWNED");
    }

    private static async Task SeedCatalog(TestApplicationDbContext context)
    {
        var categories = new Dictionary<ComponentType, Category>();
        void Add(string sku, decimal price, ComponentSpecification spec, int stock = 2, bool active = true)
        {
            if (!categories.TryGetValue(spec.ComponentType, out var category))
            {
                var type = spec.ComponentType.ToString();
                category = new Category(type, type.ToLowerInvariant(), spec.ComponentType, type);
                categories.Add(spec.ComponentType, category);
                context.Categories.Add(category);
            }
            context.Products.Add(new Product(category.Id, sku, sku.ToLowerInvariant(), sku, "Demo", price, stock,
                "https://example.com/part.png", spec.ComponentType, spec) { IsActive = active });
        }

        var cpu = new CpuSpecification { Socket = CpuSocket.AM5, CoreCount = 6, TdpWattage = 65, SupportedMemoryType = RamType.DDR5 };
        Add("CPU-OWNED", 4_000_000, cpu, stock: 0, active: false);
        Add("CPU-OFFICE", 2_000_000, cpu with { HasIntegratedGpu = true });
        var board = new MotherboardSpecification { Socket = CpuSocket.AM5, MemoryType = RamType.DDR5, FormFactor = MotherboardFormFactor.ATX };
        Add("BOARD-WRONG", 500_000, board with { Socket = CpuSocket.LGA1700 });
        Add("BOARD-AM5", 1_000_000, board);
        Add("RAM-16", 1_000_000, new RamSpecification { MemoryType = RamType.DDR5, CapacityGb = 16 });
        Add("SSD", 1_000_000, new StorageSpecification { StorageType = StorageType.NVMe_M2, CapacityGb = 1000 });
        var gpu = new GpuSpecification { VramGb = 8, LengthMm = 250, TdpWattage = 160, RecommendedPsuWattage = 550 };
        Add("GPU-TOO-LONG", 500_000, gpu with { VramGb = 16, LengthMm = 400 });
        Add("GPU-SOLD-OUT", 500_000, gpu with { VramGb = 16 }, stock: 0);
        Add("GPU-INACTIVE", 500_000, gpu with { VramGb = 16 }, active: false);
        Add("GPU-FITS", 2_000_000, gpu);
        Add("PSU", 1_000_000, new PsuSpecification { Wattage = 650 });
        Add("CASE", 500_000, new CaseSpecification
        {
            SupportedFormFactors = [MotherboardFormFactor.ATX], MaxGpuLengthMm = 300, MaxCpuCoolerHeightMm = 160
        });
        Add("COOLER", 500_000, new CoolerSpecification
        {
            CoolerType = CoolerType.Air, SupportedSockets = [CpuSocket.AM5], HeightMm = 140, MaxTdpRating = 100
        });
        await context.SaveChangesAsync();
    }
}

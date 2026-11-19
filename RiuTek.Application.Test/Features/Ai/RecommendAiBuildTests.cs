using FluentAssertions;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
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
    [Theory]
    [InlineData("gaming", "CPU-OFFICE", "GPU-UPPER", "RAM-16")]
    [InlineData("creation", "CPU-CREATION", "GPU-FITS", "RAM-32")]
    public async Task BudgetAllocation_PrioritizesDifferentPartsForTheSameBudget(
        string useCase, string expectedCpu, string expectedGpu, string expectedRam)
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        await SeedCatalog(context);
        await AddAlternatives(context);
        var reply = await new RecommendAiBuildQueryHandler(context, new HardwareCompatibilityService())
            .Handle(new RecommendAiBuildQuery(new GroqStructuredPlan("recommend_build", [],
                MaxPrice: 12_000_000, BuildUseCase: useCase)), CancellationToken.None);

        reply.Status.Should().Be("answered");
        reply.Build!.TotalVnd.Should().BeLessThanOrEqualTo(12_000_000);
        reply.Build.Items.Select(item => item.Product.Sku).Should().Contain([expectedCpu, expectedGpu, expectedRam]);
    }

    [Fact]
    public async Task Revise_KeepCpuUpgradeGpuThenLowerBudget_ReusesExactSnapshotAndRevalidates()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        await SeedCatalog(context);
        await AddAlternatives(context);
        var handler = new RecommendAiBuildQueryHandler(context, new HardwareCompatibilityService());
        var first = await handler.Handle(new RecommendAiBuildQuery(new GroqStructuredPlan("recommend_build", [],
            MaxPrice: 9_000_000, BuildUseCase: "gaming")), CancellationToken.None);
        var previous = Previous(first.Build!);
        var upgradePlan = new GroqStructuredPlan("recommend_build", [], MaxPrice: 12_000_000,
            BuildAction: "revise", KeepComponents: ["Cpu"], UpgradeComponent: "Gpu");

        var upgraded = await handler.Handle(new RecommendAiBuildQuery(upgradePlan, previous), CancellationToken.None);
        upgraded.Status.Should().Be("answered");
        upgraded.Build!.UseCase.Should().Be("gaming");
        upgraded.Build.Items.Should().ContainSingle(item => item.Product.Sku == "CPU-OFFICE" && !item.AlreadyOwned);
        upgraded.Build.Items.Should().ContainSingle(item => item.Product.Sku == "GPU-UPPER");
        upgraded.Build.TotalVnd.Should().Be(upgraded.Build.Items.Sum(item => item.Product.PriceVnd));

        var reduced = await handler.Handle(new RecommendAiBuildQuery(new GroqStructuredPlan("recommend_build", [],
            MaxPrice: 9_000_000, BuildAction: "revise"), Previous(upgraded.Build)), CancellationToken.None);
        reduced.Status.Should().Be("answered");
        reduced.Build!.BudgetVnd.Should().Be(9_000_000);
        reduced.Build.TotalVnd.Should().BeLessThanOrEqualTo(9_000_000);
        reduced.Build.Items.Select(item => item.Product.Sku).Should().Contain(["CPU-OFFICE", "GPU-FITS"]);
    }

    [Fact]
    public async Task Revise_WhenUpgradeCannotFitBudget_DoesNotReturnTheSameGpuAsAnUpgrade()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        await SeedCatalog(context);
        await AddAlternatives(context);
        var handler = new RecommendAiBuildQueryHandler(context, new HardwareCompatibilityService());
        var first = await handler.Handle(new RecommendAiBuildQuery(new GroqStructuredPlan("recommend_build", [],
            MaxPrice: 9_000_000, BuildUseCase: "gaming")), CancellationToken.None);

        var revised = await handler.Handle(new RecommendAiBuildQuery(new GroqStructuredPlan("recommend_build", [],
            BuildAction: "revise", KeepComponents: ["Cpu"], UpgradeComponent: "Gpu"), Previous(first.Build!)), CancellationToken.None);
        revised.Status.Should().Be("insufficient_data");
        revised.Build.Should().BeNull();
    }

    [Fact]
    public async Task Revise_PreservesOwnedPartAndMinimumCapacityAndChecksItsAvailabilityAgain()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        await SeedCatalog(context);
        await AddAlternatives(context);
        var handler = new RecommendAiBuildQueryHandler(context, new HardwareCompatibilityService());
        var first = await handler.Handle(new RecommendAiBuildQuery(new GroqStructuredPlan("recommend_build", [],
            MaxPrice: 12_000_000, BuildUseCase: "gaming", OwnedProducts: ["CPU-OWNED"],
            TechnicalCriteria: new Dictionary<string, string> { ["minRamGb"] = "32" })), CancellationToken.None);

        var reduced = await handler.Handle(new RecommendAiBuildQuery(new GroqStructuredPlan("recommend_build", [],
            MaxPrice: 8_000_000, BuildAction: "revise"), Previous(first.Build!)), CancellationToken.None);
        reduced.Status.Should().Be("answered");
        reduced.Build!.Items.Should().ContainSingle(item => item.Product.Sku == "CPU-OWNED" && item.AlreadyOwned);
        reduced.Build.Items.Select(item => item.Product.Sku).Should().Contain("RAM-32");
        reduced.Build.TotalVnd.Should().Be(8_000_000);

        var ram = context.Products.Single(product => product.Sku == "RAM-32");
        ram.IsActive = false;
        await context.SaveChangesAsync();
        var unavailable = await handler.Handle(new RecommendAiBuildQuery(new GroqStructuredPlan("recommend_build", [],
            BuildAction: "revise"), Previous(reduced.Build)), CancellationToken.None);
        unavailable.Build.Should().BeNull();

        var fresh = await handler.Handle(new RecommendAiBuildQuery(new GroqStructuredPlan("recommend_build", [],
            MaxPrice: 8_000_000, BuildUseCase: "office", BuildAction: "new"), Previous(first.Build!)), CancellationToken.None);
        fresh.Status.Should().Be("answered");
        fresh.Build!.Items.Should().NotContain(item => item.AlreadyOwned);
        fresh.Build.Items.Select(item => item.Product.Sku).Should().Contain(["CPU-OFFICE", "RAM-16"]);
    }

    [Fact]
    public async Task RevisionWithoutSnapshot_RequestsClarification()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var result = await new RecommendAiBuildQueryHandler(context, new HardwareCompatibilityService())
            .Handle(new RecommendAiBuildQuery(new GroqStructuredPlan("recommend_build", [],
                BuildAction: "revise", KeepComponents: ["Cpu"])), CancellationToken.None);
        result.Status.Should().Be("needs_clarification");
        result.Build.Should().BeNull();
    }

    private static AiPreviousBuildDto Previous(AiBuildDto build) => new(build.BudgetVnd, build.UseCase!,
        build.Items.Select(item => new AiPreviousBuildItemDto(item.ProductId, item.AlreadyOwned)).ToList(), build.MinRamGb, build.MinStorageGb, build.KeptComponents);

    private static async Task AddAlternatives(TestApplicationDbContext context)
    {
        var cpu = context.Products.Single(product => product.Sku == "CPU-OFFICE");
        var gpu = context.Products.Single(product => product.Sku == "GPU-FITS");
        var ram = context.Products.Single(product => product.Sku == "RAM-16");
        context.Products.AddRange(
            new Product(cpu.CategoryId, "CPU creation", "cpu-creation", "CPU-CREATION", "Demo", 4_000_000, 2, cpu.ImageUrl,
                ComponentType.Cpu, ((CpuSpecification)cpu.Specifications) with { CoreCount = 12 }),
            new Product(gpu.CategoryId, "GPU upper", "gpu-upper", "GPU-UPPER", "Demo", 4_000_000, 2, gpu.ImageUrl,
                ComponentType.Gpu, ((GpuSpecification)gpu.Specifications) with { VramGb = 12 }),
            new Product(ram.CategoryId, "RAM 32", "ram-32", "RAM-32", "Demo", 2_000_000, 2, ram.ImageUrl,
                ComponentType.Ram, ((RamSpecification)ram.Specifications) with { CapacityGb = 32 }));
        await context.SaveChangesAsync();
    }

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
             "target_products":["GPU-FITS"],"owned_products":["CPU-OWNED"],
             "build_action":"revise","keep_components":["Cpu"],"upgrade_component":"Gpu"}
            """);

        result.IsSuccess.Should().BeTrue();
        result.Value.BuildUseCase.Should().Be("gaming");
        result.Value.MaxPrice.Should().Be(20_000_000);
        result.Value.TargetProducts.Should().Equal("GPU-FITS");
        result.Value.OwnedProducts.Should().Equal("CPU-OWNED");
        result.Value.BuildAction.Should().Be("revise");
        result.Value.KeepComponents.Should().Equal("Cpu");
        result.Value.UpgradeComponent.Should().Be("Gpu");
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

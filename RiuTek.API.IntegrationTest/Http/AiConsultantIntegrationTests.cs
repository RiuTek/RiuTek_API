using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RiuTek.API.Contracts;
using RiuTek.API.IntegrationTest.Infrastructure;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;
using RiuTek.Infrastructure.Data;
using Xunit;

namespace RiuTek.API.IntegrationTest.Http;

[Collection(IntegrationTestCollection.Name)]
public class AiConsultantIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainerFixture _fixture;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public AiConsultantIntegrationTests(PostgreSqlContainerFixture fixture)
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

        var user = new User($"ai-{role}-{Guid.NewGuid():N}@riutek.test", "HashedPw123!", $"Test {role}", role);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        return jwtGenerator.GenerateAccessToken(user);
    }

    private async Task SeedSampleCatalogAsync()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var cpuCat = new Category("Vi xử lý CPU", "vi-xu-ly-cpu", ComponentType.Cpu, "CPU");
        db.Categories.Add(cpuCat);
        await db.SaveChangesAsync();

        var cpuSpec = new CpuSpecification
        {
            Socket = CpuSocket.LGA1700,
            CoreCount = 14,
            ThreadCount = 20,
            BaseClockGhz = 3.5,
            BoostClockGhz = 5.3,
            TdpWattage = 125,
            HasIntegratedGpu = true,
            SupportedMemoryType = RamType.DDR5,
            MaxMemorySpeedMhz = 5600
        };

        var cpu = new Product(
            cpuCat.Id,
            "Intel Core i5-14600K",
            "intel-core-i5-14600k",
            "CPU-INTEL-14600K",
            "Intel",
            7890000m,
            12,
            "https://cdn.riutek.test/i5-14600k.png",
            ComponentType.Cpu,
            cpuSpec);

        db.Products.Add(cpu);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Ask_DefaultDisabled_Returns503ServiceUnavailable()
    {
        using var client = _fixture.Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/ai/ask", new AskAiRequest("Cho hỏi về i5-14600K"));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Ask_InternalMode_Enforces401And403AndAllowsContentManager()
    {
        await SeedSampleCatalogAsync();
        var fakeGroq = new IntegrationFakeGroqClient();
        var fakeQuota = new IntegrationFakeQuotaService();
        var fakeSettings = new IntegrationFakeSettings
        {
            Enabled = true,
            AllowAnonymous = false,
            WebSearchEnabled = false,
            IsConfigured = true
        };

        await using var customFactory = _fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IAiRuntimeSettings>();
                services.RemoveAll<IAiQuotaService>();
                services.RemoveAll<IGroqClient>();

                services.AddSingleton<IAiRuntimeSettings>(fakeSettings);
                services.AddSingleton<IAiQuotaService>(fakeQuota);
                services.AddSingleton<IGroqClient>(fakeGroq);
            });
        });

        using var anonClient = customFactory.CreateClient();
        var anonResp = await anonClient.PostAsJsonAsync("/api/v1/ai/ask", new AskAiRequest("i5-14600K giá bao nhiêu?"));
        anonResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var customerToken = await SeedUserAndGenerateTokenAsync(UserRole.Customer);
        using var customerClient = customFactory.CreateClient();
        customerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);
        var custResp = await customerClient.PostAsJsonAsync("/api/v1/ai/ask", new AskAiRequest("i5-14600K giá bao nhiêu?"));
        custResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var adminToken = await SeedUserAndGenerateTokenAsync(UserRole.Admin);
        using var adminClient = customFactory.CreateClient();
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var adminResp = await adminClient.PostAsJsonAsync(
            "/api/v1/ai/ask",
            new AskAiRequest("i5-14600K dùng socket gì và giá bao nhiêu?", "intel-core-i5-14600k"));

        adminResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await adminResp.Content.ReadFromJsonAsync<AiConsultantResponseDto>(JsonOptions);
        dto.Should().NotBeNull();
        dto!.Status.Should().Be("answered");
        dto.Products.Should().ContainSingle(p => p.Sku == "CPU-INTEL-14600K" && p.PriceVnd == 7890000m && p.InStock);
        fakeGroq.WebCalls.Should().Be(0);
    }

    [Fact]
    public async Task Ask_PsuWattageMinimumVsExact_FiltersInPostgreSqlBeforePaginationAndExcludesInvalidRows()
    {
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var psuCat = new Category("Nguồn PSU", "nguon-psu", ComponentType.Psu, "Nguồn máy tính");
            db.Categories.Add(psuCat);
            await db.SaveChangesAsync();

            // Seed 6 cheaper 550W PSUs (in-stock, active) so if filtering happened after Take(5), 650W and 750W would be pushed out!
            for (var i = 1; i <= 6; i++)
            {
                db.Products.Add(new Product(
                    psuCat.Id,
                    $"Cheaper 550W PSU #{i}",
                    $"cheaper-550w-psu-{i}",
                    $"PSU-550-{i}",
                    "Cooler Master",
                    900000m + (i * 10000m),
                    10,
                    "https://cdn.riutek.test/psu550.png",
                    ComponentType.Psu,
                    new PsuSpecification { Wattage = 550, Efficiency = PsuEfficiency.Plus80Bronze }));
            }

            // Valid 650W PSU (in-stock, active, within 3,000,000 budget)
            db.Products.Add(new Product(
                psuCat.Id,
                "MSI MAG A650BN 650W",
                "msi-mag-a650bn-650w",
                "PSU-VALID-650W",
                "MSI",
                1490000m,
                6,
                "https://cdn.riutek.test/psu650.png",
                ComponentType.Psu,
                new PsuSpecification { Wattage = 650, Efficiency = PsuEfficiency.Plus80Bronze }));

            // Valid 750W PSU (in-stock, active, within 3,000,000 budget)
            db.Products.Add(new Product(
                psuCat.Id,
                "Corsair RM750e 750W",
                "corsair-rm750e-750w",
                "PSU-VALID-750W",
                "Corsair",
                2590000m,
                4,
                "https://cdn.riutek.test/psu750.png",
                ComponentType.Psu,
                new PsuSpecification { Wattage = 750, Efficiency = PsuEfficiency.Plus80Gold }));

            // Out-of-stock 750W PSU (within budget, active, but StockQuantity = 0)
            db.Products.Add(new Product(
                psuCat.Id,
                "DeepCool PK750D 750W OutOfStock",
                "deepcool-pk750d-750w",
                "PSU-OOS-750W",
                "DeepCool",
                1990000m,
                0,
                "https://cdn.riutek.test/psu750-oos.png",
                ComponentType.Psu,
                new PsuSpecification { Wattage = 750, Efficiency = PsuEfficiency.Plus80Bronze }));

            // Over-budget 850W PSU (in-stock, active, but Price > 3,000,000)
            db.Products.Add(new Product(
                psuCat.Id,
                "Seasonic Focus GX-850 850W",
                "seasonic-focus-gx-850",
                "PSU-OVERBUDGET-850W",
                "Seasonic",
                3690000m,
                5,
                "https://cdn.riutek.test/psu850.png",
                ComponentType.Psu,
                new PsuSpecification { Wattage = 850, Efficiency = PsuEfficiency.Plus80Gold }));

            await db.SaveChangesAsync();
        }

        var fakeGroq = new IntegrationFakeGroqClient
        {
            OnAnalyze = question =>
            {
                if (question.Contains("từ 650W trở lên", StringComparison.OrdinalIgnoreCase))
                {
                    return Result.Success(new GroqStructuredPlan(
                        TaskType: "recommend",
                        TargetProducts: [],
                        ComponentType: "Psu",
                        MaxPrice: 3000000m,
                        InStockOnly: true,
                        MinPsuWattage: 650,
                        RequiredFacts: ["wattage", "priceVnd", "inStock"]));
                }

                return Result.Success(new GroqStructuredPlan(
                    TaskType: "recommend",
                    TargetProducts: [],
                    ComponentType: "Psu",
                    MaxPrice: 3000000m,
                    InStockOnly: true,
                    ExactPsuWattage: 650,
                    RequiredFacts: ["wattage", "priceVnd", "inStock"]));
            }
        };

        var fakeQuota = new IntegrationFakeQuotaService();
        var fakeSettings = new IntegrationFakeSettings
        {
            Enabled = true,
            AllowAnonymous = false,
            WebSearchEnabled = false,
            IsConfigured = true
        };

        await using var customFactory = _fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IAiRuntimeSettings>();
                services.RemoveAll<IAiQuotaService>();
                services.RemoveAll<IGroqClient>();

                services.AddSingleton<IAiRuntimeSettings>(fakeSettings);
                services.AddSingleton<IAiQuotaService>(fakeQuota);
                services.AddSingleton<IGroqClient>(fakeGroq);
            });
        });

        var adminToken = await SeedUserAndGenerateTokenAsync(UserRole.Admin);
        using var adminClient = customFactory.CreateClient();
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        // 1. Minimum wattage >= 650W must return both PSU-VALID-650W and PSU-VALID-750W
        var minResp = await adminClient.PostAsJsonAsync(
            "/api/v1/ai/ask",
            new AskAiRequest("Gợi ý nguồn từ 650W trở lên dưới 3 triệu còn hàng"));

        minResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var minDto = await minResp.Content.ReadFromJsonAsync<AiConsultantResponseDto>(JsonOptions);
        minDto.Should().NotBeNull();
        minDto!.Status.Should().Be("answered");
        minDto.Products.Select(p => p.Sku).Should().Equal("PSU-VALID-650W", "PSU-VALID-750W");
        fakeGroq.WebCalls.Should().Be(0);

        // 2. Exact wattage 650W must return only PSU-VALID-650W
        var exactResp = await adminClient.PostAsJsonAsync(
            "/api/v1/ai/ask",
            new AskAiRequest("Gợi ý nguồn đúng 650W dưới 3 triệu còn hàng"));

        exactResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var exactDto = await exactResp.Content.ReadFromJsonAsync<AiConsultantResponseDto>(JsonOptions);
        exactDto.Should().NotBeNull();
        exactDto!.Status.Should().Be("answered");
        exactDto.Products.Select(p => p.Sku).Should().Equal("PSU-VALID-650W");
        fakeGroq.WebCalls.Should().Be(0);
    }

    private sealed class IntegrationFakeSettings : IAiRuntimeSettings
    {
        public bool Enabled { get; set; } = true;
        public bool AllowAnonymous { get; set; } = true;
        public bool WebSearchEnabled { get; set; }
        public bool IsConfigured { get; set; } = true;
    }

    private sealed class IntegrationFakeQuotaService : IAiQuotaService
    {
        public Task<QuotaReservationResult> ReserveTurnAsync(string clientKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(new QuotaReservationResult(true, RetryAfterSeconds: 900));

        public Task<QuotaReservationResult> ReserveProviderRequestAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new QuotaReservationResult(true, RetryAfterSeconds: 3600));

        public Task<QuotaReservationResult> ReserveWebRequestAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new QuotaReservationResult(true, RetryAfterSeconds: 3600));
    }

    private sealed class IntegrationFakeGroqClient : IGroqClient
    {
        public int WebCalls { get; private set; }
        public Func<string, Result<GroqStructuredPlan>>? OnAnalyze { get; set; }

        public Task<Result<GroqStructuredPlan>> AnalyzeQuestionAsync(
            string question,
            string? productSlug,
            IReadOnlyList<ChatMessageDto>? history,
            CancellationToken cancellationToken = default)
        {
            if (OnAnalyze != null)
            {
                return Task.FromResult(OnAnalyze(question));
            }

            return Task.FromResult(Result.Success(new GroqStructuredPlan(
                TaskType: "product_info",
                TargetProducts: ["CPU-INTEL-14600K"],
                RequiredFacts: ["socket", "priceVnd", "inStock"])));
        }

        public Task<Result<IReadOnlyList<GroqWebSearchResult>>> SearchWebForTechnicalFactsAsync(
            string brandAndModel,
            IReadOnlyList<string> missingFacts,
            CancellationToken cancellationToken = default)
        {
            WebCalls++;
            return Task.FromResult(Result.Success<IReadOnlyList<GroqWebSearchResult>>([]));
        }

        public Task<Result<GroqSynthesisResult>> SynthesizeAnswerAsync(
            GroqSynthesisRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Success(new GroqSynthesisResult(
                Status: "answered",
                Answer: "Thông tin tư vấn đã xác minh từ danh mục RiuTek.",
                SupportingFactKeys: ["socket", "wattage", "priceVnd", "inStock"])));
        }
    }
}

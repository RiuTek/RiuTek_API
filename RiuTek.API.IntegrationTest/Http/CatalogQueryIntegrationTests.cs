using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using RiuTek.API.Contracts;
using RiuTek.API.IntegrationTest.Infrastructure;
using RiuTek.Application.Common.Models;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Products.Queries;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;
using Xunit;

namespace RiuTek.API.IntegrationTest.Http;

[Collection(IntegrationTestCollection.Name)]
public class CatalogQueryIntegrationTests : CatalogIntegrationTestBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public CatalogQueryIntegrationTests(PostgreSqlContainerFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task GetProducts_SearchAndBrand_AreTrimmedCaseInsensitiveAndUseExpectedFields()
    {
        var catalog = await SeedStandardCatalogAsync();
        using var publicClient = CreatePublicClient();

        // 1. Name-only search: "  box  " (token only in P1 Name "Intel Core i5-12400F Box")
        using var searchNameResp = await publicClient.GetAsync($"/api/v1/products?searchTerm={Uri.EscapeDataString("  box  ")}");
        searchNameResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var searchNameResult = (await searchNameResp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        searchNameResult.TotalCount.Should().Be(1);
        searchNameResult.Items.Should().HaveCount(1);
        searchNameResult.Items.Single().Id.Should().Be(catalog.P1.Id);
        searchNameResult.Items.Single().Name.ToLowerInvariant().Should().Contain("box");

        // 2. SKU-only search: "  token999  " (token only in P3 SKU "SKU-CPU-AMD-7600-TOKEN999")
        using var searchSkuResp = await publicClient.GetAsync($"/api/v1/products?searchTerm={Uri.EscapeDataString("  token999  ")}");
        searchSkuResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var searchSkuResult = (await searchSkuResp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        searchSkuResult.TotalCount.Should().Be(1);
        searchSkuResult.Items.Should().HaveCount(1);
        searchSkuResult.Items.Single().Id.Should().Be(catalog.P3.Id);
        searchSkuResult.Items.Single().Sku.Should().Be(catalog.P3.Sku);

        // 3. Brand-only search: "  asustek  " (token only in P6 Brand "ASUSTeK", not in Name or SKU)
        using var searchBrandResp = await publicClient.GetAsync($"/api/v1/products?searchTerm={Uri.EscapeDataString("  asustek  ")}");
        searchBrandResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var searchBrandResult = (await searchBrandResp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        searchBrandResult.TotalCount.Should().Be(1);
        searchBrandResult.Items.Should().HaveCount(1);
        searchBrandResult.Items.Single().Id.Should().Be(catalog.P6.Id);
        searchBrandResult.Items.Single().Brand.Should().Be("ASUSTeK");

        // 4. Whitespace-only search: "%20%20" (must ignore search and return all 7 products)
        using var searchWsResp = await publicClient.GetAsync("/api/v1/products?searchTerm=%20%20");
        searchWsResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var searchWsResult = (await searchWsResp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        searchWsResult.TotalCount.Should().Be(7);
        searchWsResult.Items.Should().HaveCount(7);
        searchWsResult.Items.Select(p => p.Id).Should().BeEquivalentTo(
            [catalog.P1.Id, catalog.P2.Id, catalog.P3.Id, catalog.P4.Id, catalog.P5.Id, catalog.P6.Id, catalog.P7.Id]);

        // 5. Exact Brand filter:
        // Case A: "  asustek  " -> exact P6
        using var brandAsustekResp = await publicClient.GetAsync($"/api/v1/products?brand={Uri.EscapeDataString("  asustek  ")}");
        brandAsustekResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var brandAsustekResult = (await brandAsustekResp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        brandAsustekResult.TotalCount.Should().Be(1);
        brandAsustekResult.Items.Single().Id.Should().Be(catalog.P6.Id);

        // Case B: "  asus  " -> exact P5 (P6 has Brand ASUSTeK)
        using var brandAsusResp = await publicClient.GetAsync($"/api/v1/products?brand={Uri.EscapeDataString("  asus  ")}");
        brandAsusResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var brandAsusResult = (await brandAsusResp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        brandAsusResult.TotalCount.Should().Be(1);
        brandAsusResult.Items.Single().Id.Should().Be(catalog.P5.Id);

        // Case C: "  intel  " -> exact P1, P2, P7
        using var brandIntelResp = await publicClient.GetAsync($"/api/v1/products?brand={Uri.EscapeDataString("  intel  ")}");
        brandIntelResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var brandIntelResult = (await brandIntelResp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        brandIntelResult.TotalCount.Should().Be(3);
        brandIntelResult.Items.Should().HaveCount(3);
        brandIntelResult.Items.Select(p => p.Id).Should().BeEquivalentTo([catalog.P1.Id, catalog.P2.Id, catalog.P7.Id]);
        brandIntelResult.Items.Should().AllSatisfy(p => p.Brand.Should().Be("Intel"));

        // Case D: Negative substring brand "inte" -> 0 matches
        using var brandSubResp = await publicClient.GetAsync("/api/v1/products?brand=inte");
        brandSubResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var brandSubResult = (await brandSubResp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        brandSubResult.TotalCount.Should().Be(0);
        brandSubResult.Items.Should().BeEmpty();

        // 6. Metadata and representative payload assertions on whitespace response
        searchWsResult.PageIndex.Should().Be(1);
        searchWsResult.PageSize.Should().Be(20);
        searchWsResult.TotalPages.Should().Be(1);
        var p1Summary = searchWsResult.Items.Single(p => p.Id == catalog.P1.Id);
        p1Summary.CategoryName.Should().Be("Intel & AMD Processors Root");
        p1Summary.Name.Should().Be("Intel Core i5-12400F Box");
        p1Summary.Sku.Should().Be("SKU-CPU-INTEL-12400F");
        p1Summary.Brand.Should().Be("Intel");
    }

    [Fact]
    public async Task GetProducts_ScalarAndCombinedFilters_ApplyInclusiveAndSemantics()
    {
        var catalog = await SeedStandardCatalogAsync();
        using var publicClient = CreatePublicClient();

        // 1. Default visibility without isActive: includes both active (5) and inactive (P4, P7) -> 7 total
        using var defaultResp = await publicClient.GetAsync("/api/v1/products");
        defaultResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var defaultResult = (await defaultResp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        defaultResult.TotalCount.Should().Be(7);
        defaultResult.Items.Should().HaveCount(7);
        defaultResult.Items.Select(p => p.Id).Should().BeEquivalentTo(
            [catalog.P1.Id, catalog.P2.Id, catalog.P3.Id, catalog.P4.Id, catalog.P5.Id, catalog.P6.Id, catalog.P7.Id]);
        defaultResult.Items.Where(p => p.IsActive).Should().HaveCount(5);
        defaultResult.Items.Where(p => !p.IsActive).Select(p => p.Id).Should().BeEquivalentTo([catalog.P4.Id, catalog.P7.Id]);

        // 2. ComponentType = Cpu: P1, P2, P3, P4, P7
        using var cpuResp = await publicClient.GetAsync("/api/v1/products?componentType=Cpu");
        cpuResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var cpuResult = (await cpuResp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        cpuResult.TotalCount.Should().Be(5);
        cpuResult.Items.Select(p => p.Id).Should().BeEquivalentTo(
            [catalog.P1.Id, catalog.P2.Id, catalog.P3.Id, catalog.P4.Id, catalog.P7.Id]);
        cpuResult.Items.Should().AllSatisfy(p => p.ComponentType.Should().Be(ComponentType.Cpu));

        // 3. ComponentType = Gpu: P5, P6
        using var gpuResp = await publicClient.GetAsync("/api/v1/products?componentType=Gpu");
        gpuResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var gpuResult = (await gpuResp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        gpuResult.TotalCount.Should().Be(2);
        gpuResult.Items.Select(p => p.Id).Should().BeEquivalentTo([catalog.P5.Id, catalog.P6.Id]);
        gpuResult.Items.Should().AllSatisfy(p => p.ComponentType.Should().Be(ComponentType.Gpu));

        // 4. MinPrice = 6M and MaxPrice = 10M (inclusive bounds: P3=6M, P5=8M, P6=8M, P2=10M)
        using var priceResp = await publicClient.GetAsync("/api/v1/products?minPrice=6000000&maxPrice=10000000");
        priceResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var priceResult = (await priceResp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        priceResult.TotalCount.Should().Be(4);
        priceResult.Items.Select(p => p.Id).Should().BeEquivalentTo([catalog.P2.Id, catalog.P3.Id, catalog.P5.Id, catalog.P6.Id]);
        priceResult.Items.Should().AllSatisfy(p => p.Price.Should().BeInRange(6000000m, 10000000m));

        // 5. InStock = true (StockQuantity > 0: P1=15, P2=8, P4=5, P5=12, P6=20)
        using var inStockTrueResp = await publicClient.GetAsync("/api/v1/products?inStock=true");
        inStockTrueResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var inStockTrueResult = (await inStockTrueResp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        inStockTrueResult.TotalCount.Should().Be(5);
        inStockTrueResult.Items.Select(p => p.Id).Should().BeEquivalentTo([catalog.P1.Id, catalog.P2.Id, catalog.P4.Id, catalog.P5.Id, catalog.P6.Id]);
        inStockTrueResult.Items.Should().AllSatisfy(p => p.StockQuantity.Should().BeGreaterThan(0));

        // 6. InStock = false (StockQuantity == 0: P3=0, P7=0)
        using var inStockFalseResp = await publicClient.GetAsync("/api/v1/products?inStock=false");
        inStockFalseResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var inStockFalseResult = (await inStockFalseResp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        inStockFalseResult.TotalCount.Should().Be(2);
        inStockFalseResult.Items.Select(p => p.Id).Should().BeEquivalentTo([catalog.P3.Id, catalog.P7.Id]);
        inStockFalseResult.Items.Should().AllSatisfy(p => p.StockQuantity.Should().Be(0));

        // 7. IsActive = true (P1, P2, P3, P5, P6)
        using var activeTrueResp = await publicClient.GetAsync("/api/v1/products?isActive=true");
        activeTrueResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var activeTrueResult = (await activeTrueResp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        activeTrueResult.TotalCount.Should().Be(5);
        activeTrueResult.Items.Select(p => p.Id).Should().BeEquivalentTo([catalog.P1.Id, catalog.P2.Id, catalog.P3.Id, catalog.P5.Id, catalog.P6.Id]);
        activeTrueResult.Items.Should().AllSatisfy(p => p.IsActive.Should().BeTrue());

        // 8. IsActive = false (P4, P7)
        using var activeFalseResp = await publicClient.GetAsync("/api/v1/products?isActive=false");
        activeFalseResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var activeFalseResult = (await activeFalseResp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        activeFalseResult.TotalCount.Should().Be(2);
        activeFalseResult.Items.Select(p => p.Id).Should().BeEquivalentTo([catalog.P4.Id, catalog.P7.Id]);
        activeFalseResult.Items.Should().AllSatisfy(p => p.IsActive.Should().BeFalse());

        // 9. Full Combined filter: SearchTerm + Brand + ComponentType + MinPrice + MaxPrice + InStock + IsActive -> P1 only
        using var combinedResp = await publicClient.GetAsync(
            "/api/v1/products?searchTerm=box&brand=Intel&componentType=Cpu&minPrice=2000000&maxPrice=8000000&inStock=true&isActive=true");
        combinedResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var combinedResult = (await combinedResp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        combinedResult.TotalCount.Should().Be(1);
        combinedResult.Items.Should().HaveCount(1);
        var single = combinedResult.Items.Single();
        single.Id.Should().Be(catalog.P1.Id);
        single.Name.ToLowerInvariant().Should().Contain("box");
        single.Brand.Should().Be("Intel");
        single.ComponentType.Should().Be(ComponentType.Cpu);
        single.Price.Should().Be(2500000m);
        single.StockQuantity.Should().Be(15);
        single.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task GetProducts_CategoryFilter_IncludesSelfAndAllDescendantsButExcludesSiblingBranchesOutsideScope()
    {
        var catalog = await SeedStandardCatalogAsync();
        using var publicClient = CreatePublicClient();

        // 1. Root CPU: includes Root CPU (P1, P7), Child CPU (P2), Grandchild CPU (P3), Sibling CPU branch (P4) -> 5 products
        using var rootResp = await publicClient.GetAsync($"/api/v1/products?categoryId={catalog.RootCpu.Id}");
        rootResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var rootResult = (await rootResp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        rootResult.TotalCount.Should().Be(5);
        rootResult.Items.Select(p => p.Id).Should().BeEquivalentTo(
            [catalog.P1.Id, catalog.P2.Id, catalog.P3.Id, catalog.P4.Id, catalog.P7.Id]);

        // 2. Child CPU: includes Child CPU (P2), Grandchild CPU (P3); excludes Root CPU (P1, P7) and Sibling CPU (P4) -> 2 products
        using var childResp = await publicClient.GetAsync($"/api/v1/products?categoryId={catalog.ChildCpu.Id}");
        childResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var childResult = (await childResp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        childResult.TotalCount.Should().Be(2);
        childResult.Items.Select(p => p.Id).Should().BeEquivalentTo([catalog.P2.Id, catalog.P3.Id]);

        // 3. Grandchild CPU: includes Grandchild CPU only (P3)
        using var grandchildResp = await publicClient.GetAsync($"/api/v1/products?categoryId={catalog.GrandchildCpu.Id}");
        grandchildResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var grandchildResult = (await grandchildResp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        grandchildResult.TotalCount.Should().Be(1);
        grandchildResult.Items.Single().Id.Should().Be(catalog.P3.Id);

        // 4. Sibling CPU (sibling branch under Root): includes Sibling CPU only (P4); excludes Root, Child, Grandchild
        using var siblingResp = await publicClient.GetAsync($"/api/v1/products?categoryId={catalog.SiblingCpu.Id}");
        siblingResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var siblingResult = (await siblingResp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        siblingResult.TotalCount.Should().Be(1);
        siblingResult.Items.Single().Id.Should().Be(catalog.P4.Id);

        // 5. Root GPU: includes Root GPU only (P5, P6)
        using var gpuCatResp = await publicClient.GetAsync($"/api/v1/products?categoryId={catalog.RootGpu.Id}");
        gpuCatResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var gpuCatResult = (await gpuCatResp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        gpuCatResult.TotalCount.Should().Be(2);
        gpuCatResult.Items.Select(p => p.Id).Should().BeEquivalentTo([catalog.P5.Id, catalog.P6.Id]);
        gpuCatResult.Items.Should().AllSatisfy(p => p.ComponentType.Should().Be(ComponentType.Gpu));

        // 6. Empty CPU category: valid category without products -> 200 OK, empty items, TotalCount = 0, TotalPages = 0
        using var emptyCatResp = await publicClient.GetAsync($"/api/v1/products?categoryId={catalog.EmptyCpu.Id}");
        emptyCatResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var emptyCatResult = (await emptyCatResp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        emptyCatResult.TotalCount.Should().Be(0);
        emptyCatResult.TotalPages.Should().Be(0);
        emptyCatResult.Items.Should().BeEmpty();

        // 7. Non-existent Category ID -> 404 Product.CategoryNotFound
        using var nonExistentResp = await publicClient.GetAsync($"/api/v1/products?categoryId={Guid.NewGuid()}");
        nonExistentResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var nonExistentError = await nonExistentResp.Content.ReadFromJsonAsync<BusinessErrorResponse>(JsonOptions);
        nonExistentError.Should().NotBeNull();
        nonExistentError!.Code.Should().Be("Product.CategoryNotFound");
    }

    [Fact]
    public async Task GetProducts_AllSortOptions_AreDeterministicOnPostgreSql()
    {
        var catalog = await SeedStandardCatalogAsync();
        using var publicClient = CreatePublicClient();

        // 1. PriceLowToHigh: Price ascending, tie broken by Name.ToLower() ascending, then Id
        // Expected: P1(2.5M) -> P7(5M) -> P3(6M) -> P5(8M ASUS White) -> P6(8M ASUSTeK Black) -> P2(10M) -> P4(14M)
        using var lowToHighResp1 = await publicClient.GetAsync($"/api/v1/products?sortBy={ProductSortOption.PriceLowToHigh}");
        lowToHighResp1.StatusCode.Should().Be(HttpStatusCode.OK);
        var lowToHighResult1 = (await lowToHighResp1.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        lowToHighResult1.TotalCount.Should().Be(7);
        lowToHighResult1.Items.Select(p => p.Id).Should().Equal(
            catalog.P1.Id, catalog.P7.Id, catalog.P3.Id, catalog.P5.Id, catalog.P6.Id, catalog.P2.Id, catalog.P4.Id);
        lowToHighResult1.Items.Select(p => p.Price).Should().BeInAscendingOrder();

        // Repeated query for PriceLowToHigh to prove deterministic tie-breaking and order stability
        using var lowToHighResp2 = await publicClient.GetAsync($"/api/v1/products?sortBy={ProductSortOption.PriceLowToHigh}");
        lowToHighResp2.StatusCode.Should().Be(HttpStatusCode.OK);
        var lowToHighResult2 = (await lowToHighResp2.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        lowToHighResult2.Items.Select(p => p.Id).Should().Equal(lowToHighResult1.Items.Select(p => p.Id));

        // 2. PriceHighToLow: Price descending, tie broken by Name.ToLower() ascending, then Id
        // Expected: P4(14M) -> P2(10M) -> P5(8M White) -> P6(8M Black) -> P3(6M) -> P7(5M) -> P1(2.5M)
        using var highToLowResp = await publicClient.GetAsync($"/api/v1/products?sortBy={ProductSortOption.PriceHighToLow}");
        highToLowResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var highToLowResult = (await highToLowResp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        highToLowResult.TotalCount.Should().Be(7);
        highToLowResult.Items.Select(p => p.Id).Should().Equal(
            catalog.P4.Id, catalog.P2.Id, catalog.P5.Id, catalog.P6.Id, catalog.P3.Id, catalog.P7.Id, catalog.P1.Id);
        highToLowResult.Items.Select(p => p.Price).Should().BeInDescendingOrder();

        // 3. NameAToZ: Name.ToLower() ascending, tie broken by Id
        // Expected: P3 (amd ryzen 5 7600 gaming)
        //       -> P4 (amd threadripper 7980x)
        //       -> P5 (asus dual rtx 4060 white)
        //       -> P6 (asus tuf rtx 4060 black)
        //       -> P7 (intel core i3-12100 special)
        //       -> P1 (intel core i5-12400f box)
        //       -> P2 (intel core i7-14700k tray)
        using var aToZResp = await publicClient.GetAsync($"/api/v1/products?sortBy={ProductSortOption.NameAToZ}");
        aToZResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var aToZResult = (await aToZResp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        aToZResult.TotalCount.Should().Be(7);
        aToZResult.Items.Select(p => p.Id).Should().Equal(
            catalog.P3.Id, catalog.P4.Id, catalog.P5.Id, catalog.P6.Id, catalog.P7.Id, catalog.P1.Id, catalog.P2.Id);
        aToZResult.Items.Select(p => p.Name.ToLowerInvariant()).Should().BeInAscendingOrder();

        // 4. NameZToA: Name.ToLower() descending, tie broken by Id
        // Expected reverse of NameAToZ: P2 -> P1 -> P7 -> P6 -> P5 -> P4 -> P3
        using var zToAResp = await publicClient.GetAsync($"/api/v1/products?sortBy={ProductSortOption.NameZToA}");
        zToAResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var zToARResult = (await zToAResp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        zToARResult.TotalCount.Should().Be(7);
        zToARResult.Items.Select(p => p.Id).Should().Equal(
            catalog.P2.Id, catalog.P1.Id, catalog.P7.Id, catalog.P6.Id, catalog.P5.Id, catalog.P4.Id, catalog.P3.Id);
        zToARResult.Items.Select(p => p.Name.ToLowerInvariant()).Should().BeInDescendingOrder();

        // 5. Default sort (no sortBy parameter) vs Explicit Newest: must produce identical complete ID order
        using var explicitNewestResp = await publicClient.GetAsync($"/api/v1/products?sortBy={ProductSortOption.Newest}");
        explicitNewestResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var explicitNewestResult = (await explicitNewestResp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        explicitNewestResult.TotalCount.Should().Be(7);

        using var defaultSortResp = await publicClient.GetAsync("/api/v1/products");
        defaultSortResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var defaultSortResult = (await defaultSortResp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        defaultSortResult.TotalCount.Should().Be(7);

        defaultSortResult.Items.Select(p => p.Id).Should().Equal(explicitNewestResult.Items.Select(p => p.Id));
        explicitNewestResult.Items.Select(p => p.CreatedAt).Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task GetProducts_Pagination_IsStableAndHandlesBeyondEndAndHugeOffsets()
    {
        var catalog = await SeedStandardCatalogAsync();
        using var publicClient = CreatePublicClient();

        // 1. Full baseline result (pageSize = 50, sortBy = PriceLowToHigh)
        using var fullResp = await publicClient.GetAsync($"/api/v1/products?pageIndex=1&pageSize=50&sortBy={ProductSortOption.PriceLowToHigh}");
        fullResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var fullResult = (await fullResp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        fullResult.TotalCount.Should().Be(7);
        fullResult.TotalPages.Should().Be(1);
        fullResult.PageIndex.Should().Be(1);
        fullResult.PageSize.Should().Be(50);
        var fullIds = fullResult.Items.Select(p => p.Id).ToList();
        fullIds.Should().Equal(catalog.P1.Id, catalog.P7.Id, catalog.P3.Id, catalog.P5.Id, catalog.P6.Id, catalog.P2.Id, catalog.P4.Id);

        // 2. Deterministic multi-page iteration (pageSize = 3, sortBy = PriceLowToHigh)
        using var page1Resp = await publicClient.GetAsync($"/api/v1/products?pageIndex=1&pageSize=3&sortBy={ProductSortOption.PriceLowToHigh}");
        page1Resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var page1 = (await page1Resp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        page1.TotalCount.Should().Be(7);
        page1.TotalPages.Should().Be(3);
        page1.PageIndex.Should().Be(1);
        page1.PageSize.Should().Be(3);
        page1.HasPreviousPage.Should().BeFalse();
        page1.HasNextPage.Should().BeTrue();
        page1.Items.Select(p => p.Id).Should().Equal(catalog.P1.Id, catalog.P7.Id, catalog.P3.Id);

        using var page2Resp = await publicClient.GetAsync($"/api/v1/products?pageIndex=2&pageSize=3&sortBy={ProductSortOption.PriceLowToHigh}");
        page2Resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var page2 = (await page2Resp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        page2.TotalCount.Should().Be(7);
        page2.TotalPages.Should().Be(3);
        page2.PageIndex.Should().Be(2);
        page2.PageSize.Should().Be(3);
        page2.HasPreviousPage.Should().BeTrue();
        page2.HasNextPage.Should().BeTrue();
        page2.Items.Select(p => p.Id).Should().Equal(catalog.P5.Id, catalog.P6.Id, catalog.P2.Id);

        using var page3Resp = await publicClient.GetAsync($"/api/v1/products?pageIndex=3&pageSize=3&sortBy={ProductSortOption.PriceLowToHigh}");
        page3Resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var page3 = (await page3Resp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        page3.TotalCount.Should().Be(7);
        page3.TotalPages.Should().Be(3);
        page3.PageIndex.Should().Be(3);
        page3.PageSize.Should().Be(3);
        page3.HasPreviousPage.Should().BeTrue();
        page3.HasNextPage.Should().BeFalse();
        page3.Items.Select(p => p.Id).Should().Equal(catalog.P4.Id);

        // 3. Union across pages: matches full result ID order, count 7, unique items, non-overlapping
        var allPagedIds = page1.Items.Concat(page2.Items).Concat(page3.Items).Select(p => p.Id).ToList();
        allPagedIds.Should().Equal(fullIds);
        allPagedIds.Should().HaveCount(7).And.OnlyHaveUniqueItems();

        page1.Items.Select(p => p.Id).Intersect(page2.Items.Select(p => p.Id)).Should().BeEmpty();
        page2.Items.Select(p => p.Id).Intersect(page3.Items.Select(p => p.Id)).Should().BeEmpty();
        page1.Items.Select(p => p.Id).Intersect(page3.Items.Select(p => p.Id)).Should().BeEmpty();

        // 4. Beyond-end page: pageIndex = 4 (offset = 3 * 3 = 9 >= 7)
        using var page4Resp = await publicClient.GetAsync($"/api/v1/products?pageIndex=4&pageSize=3&sortBy={ProductSortOption.PriceLowToHigh}");
        page4Resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var page4 = (await page4Resp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        page4.TotalCount.Should().Be(7);
        page4.TotalPages.Should().Be(3);
        page4.PageIndex.Should().Be(4);
        page4.PageSize.Should().Be(3);
        page4.Items.Should().BeEmpty();
        page4.HasPreviousPage.Should().BeTrue();
        page4.HasNextPage.Should().BeFalse();

        // 5. Huge offset beyond total count: pageIndex = 100, pageSize = 10
        using var hugePageResp = await publicClient.GetAsync("/api/v1/products?pageIndex=100&pageSize=10");
        hugePageResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var hugePage = (await hugePageResp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        hugePage.TotalCount.Should().Be(7);
        hugePage.TotalPages.Should().Be(1);
        hugePage.PageIndex.Should().Be(100);
        hugePage.PageSize.Should().Be(10);
        hugePage.Items.Should().BeEmpty();
        hugePage.HasPreviousPage.Should().BeTrue();
        hugePage.HasNextPage.Should().BeFalse();

        // 6. Int.MaxValue offset: must return 200 OK with empty items, HasPreviousPage true, HasNextPage false
        using var overflowResp = await publicClient.GetAsync($"/api/v1/products?pageIndex={int.MaxValue}&pageSize=50");
        overflowResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var overflowResult = (await overflowResp.Content.ReadFromJsonAsync<PagedResult<ProductSummaryDto>>(JsonOptions))!;
        overflowResult.TotalCount.Should().Be(7);
        overflowResult.TotalPages.Should().Be(1);
        overflowResult.PageIndex.Should().Be(int.MaxValue);
        overflowResult.PageSize.Should().Be(50);
        overflowResult.Items.Should().BeEmpty();
        overflowResult.HasPreviousPage.Should().BeTrue();
        overflowResult.HasNextPage.Should().BeFalse();

        // 7. Boundary validation checks: pageIndex < 1 or pageSize not in [1, 50]
        using var invalidPageIndexResp = await publicClient.GetAsync("/api/v1/products?pageIndex=0&pageSize=10");
        invalidPageIndexResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var invalidPageSizeZeroResp = await publicClient.GetAsync("/api/v1/products?pageIndex=1&pageSize=0");
        invalidPageSizeZeroResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var invalidPageSizeExcessResp = await publicClient.GetAsync("/api/v1/products?pageIndex=1&pageSize=51");
        invalidPageSizeExcessResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task<StandardCatalogFixture> SeedStandardCatalogAsync()
    {
        using var adminClient = CreateAdminClient();
        using var staffClient = CreateStaffClient();

        // 1. Categories
        using var rootCpuResp = await adminClient.PostAsJsonAsync("/api/v1/categories", new CreateCategoryRequest(
            Name: "Intel & AMD Processors Root",
            ComponentType: ComponentType.Cpu,
            Description: "Root CPU Category",
            ParentId: null
        ));
        rootCpuResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var rootCpu = (await rootCpuResp.Content.ReadFromJsonAsync<CategoryDto>(JsonOptions))!;

        using var childCpuResp = await staffClient.PostAsJsonAsync("/api/v1/categories", new CreateCategoryRequest(
            Name: "Desktop Core Processors Child",
            ComponentType: ComponentType.Cpu,
            Description: "Child CPU Category",
            ParentId: rootCpu.Id
        ));
        childCpuResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var childCpu = (await childCpuResp.Content.ReadFromJsonAsync<CategoryDto>(JsonOptions))!;

        using var grandchildCpuResp = await staffClient.PostAsJsonAsync("/api/v1/categories", new CreateCategoryRequest(
            Name: "Overclocked High-End Processors Grandchild",
            ComponentType: ComponentType.Cpu,
            Description: "Grandchild CPU Category",
            ParentId: childCpu.Id
        ));
        grandchildCpuResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var grandchildCpu = (await grandchildCpuResp.Content.ReadFromJsonAsync<CategoryDto>(JsonOptions))!;

        // SiblingCpu: sibling branch under rootCpu (ParentId = rootCpu.Id)
        using var siblingCpuResp = await adminClient.PostAsJsonAsync("/api/v1/categories", new CreateCategoryRequest(
            Name: "Server & Workstation Processors Sibling",
            ComponentType: ComponentType.Cpu,
            Description: "Sibling CPU Category",
            ParentId: rootCpu.Id
        ));
        siblingCpuResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var siblingCpu = (await siblingCpuResp.Content.ReadFromJsonAsync<CategoryDto>(JsonOptions))!;

        using var rootGpuResp = await adminClient.PostAsJsonAsync("/api/v1/categories", new CreateCategoryRequest(
            Name: "Graphics Cards Root",
            ComponentType: ComponentType.Gpu,
            Description: "Root GPU Category",
            ParentId: null
        ));
        rootGpuResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var rootGpu = (await rootGpuResp.Content.ReadFromJsonAsync<CategoryDto>(JsonOptions))!;

        // EmptyCpu: valid category with no products
        using var emptyCpuResp = await adminClient.PostAsJsonAsync("/api/v1/categories", new CreateCategoryRequest(
            Name: "Empty CPU Category For Query Test",
            ComponentType: ComponentType.Cpu,
            Description: "Empty Category with no products",
            ParentId: null
        ));
        emptyCpuResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var emptyCpu = (await emptyCpuResp.Content.ReadFromJsonAsync<CategoryDto>(JsonOptions))!;

        // 2. Products
        // P1: CPU Root, Intel, 2.5M, Stock 15, Active (Name contains "Box")
        using var p1Resp = await adminClient.PostAsJsonAsync("/api/v1/products", new CreateProductRequest(
            CategoryId: rootCpu.Id,
            Name: "Intel Core i5-12400F Box",
            Sku: "SKU-CPU-INTEL-12400F",
            Brand: "Intel",
            Price: 2500000m,
            OriginalPrice: 3000000m,
            StockQuantity: 15,
            ImageUrl: "https://example.com/p1.jpg",
            AdditionalImages: null,
            ComponentType: ComponentType.Cpu,
            Specifications: CreateSampleCpuSpec(CpuSocket.LGA1700, 2.5, 4.4)
        ));
        p1Resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var p1 = (await p1Resp.Content.ReadFromJsonAsync<ProductDto>(JsonOptions))!;

        // P2: CPU Child, Intel, 10M, Stock 8, Active
        using var p2Resp = await adminClient.PostAsJsonAsync("/api/v1/products", new CreateProductRequest(
            CategoryId: childCpu.Id,
            Name: "Intel Core i7-14700K Tray",
            Sku: "SKU-CPU-INTEL-14700K",
            Brand: "Intel",
            Price: 10000000m,
            OriginalPrice: 11000000m,
            StockQuantity: 8,
            ImageUrl: "https://example.com/p2.jpg",
            AdditionalImages: null,
            ComponentType: ComponentType.Cpu,
            Specifications: CreateSampleCpuSpec(CpuSocket.LGA1700, 3.4, 5.6)
        ));
        p2Resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var p2 = (await p2Resp.Content.ReadFromJsonAsync<ProductDto>(JsonOptions))!;

        // P3: CPU Grandchild, AMD, 6M, Stock 0, Active (contains SKU token TOKEN999)
        using var p3Resp = await adminClient.PostAsJsonAsync("/api/v1/products", new CreateProductRequest(
            CategoryId: grandchildCpu.Id,
            Name: "AMD Ryzen 5 7600 Gaming",
            Sku: "SKU-CPU-AMD-7600-TOKEN999",
            Brand: "AMD",
            Price: 6000000m,
            OriginalPrice: null,
            StockQuantity: 0,
            ImageUrl: "https://example.com/p3.jpg",
            AdditionalImages: null,
            ComponentType: ComponentType.Cpu,
            Specifications: CreateSampleCpuSpec(CpuSocket.AM5, 3.8, 5.1)
        ));
        p3Resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var p3 = (await p3Resp.Content.ReadFromJsonAsync<ProductDto>(JsonOptions))!;

        // P4: CPU Sibling, AMD, 14M, Stock 5, Inactive (updated via PUT)
        using var p4CreateResp = await adminClient.PostAsJsonAsync("/api/v1/products", new CreateProductRequest(
            CategoryId: siblingCpu.Id,
            Name: "AMD Threadripper 7980X",
            Sku: "SKU-CPU-AMD-7980X",
            Brand: "AMD",
            Price: 14000000m,
            OriginalPrice: 16000000m,
            StockQuantity: 5,
            ImageUrl: "https://example.com/p4.jpg",
            AdditionalImages: null,
            ComponentType: ComponentType.Cpu,
            Specifications: CreateSampleCpuSpec(CpuSocket.sTRX4, 3.2, 5.1)
        ));
        p4CreateResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var p4Created = (await p4CreateResp.Content.ReadFromJsonAsync<ProductDto>(JsonOptions))!;

        using var p4UpdateResp = await staffClient.PutAsJsonAsync($"/api/v1/products/{p4Created.Id}", new UpdateProductRequest(
            CategoryId: p4Created.CategoryId,
            Name: p4Created.Name,
            Sku: p4Created.Sku,
            Brand: p4Created.Brand,
            Price: p4Created.Price,
            OriginalPrice: p4Created.OriginalPrice,
            StockQuantity: p4Created.StockQuantity,
            IsActive: false,
            ImageUrl: p4Created.ImageUrl,
            AdditionalImages: p4Created.AdditionalImages,
            ComponentType: p4Created.ComponentType,
            Specifications: p4Created.Specifications
        ));
        p4UpdateResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var p4 = (await p4UpdateResp.Content.ReadFromJsonAsync<ProductDto>(JsonOptions))!;

        // P5: GPU Root, ASUS, 8M, Stock 12, Active (Name: ASUS Dual RTX 4060 White)
        using var p5Resp = await adminClient.PostAsJsonAsync("/api/v1/products", new CreateProductRequest(
            CategoryId: rootGpu.Id,
            Name: "ASUS Dual RTX 4060 White",
            Sku: "SKU-GPU-ASUS-4060W",
            Brand: "ASUS",
            Price: 8000000m,
            OriginalPrice: 8500000m,
            StockQuantity: 12,
            ImageUrl: "https://example.com/p5.jpg",
            AdditionalImages: null,
            ComponentType: ComponentType.Gpu,
            Specifications: CreateSampleGpuSpec()
        ));
        p5Resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var p5 = (await p5Resp.Content.ReadFromJsonAsync<ProductDto>(JsonOptions))!;

        // P6: GPU Root, Brand: ASUSTeK (unique brand-only token), 8M, Stock 20, Active (Name: ASUS TUF RTX 4060 Black)
        using var p6Resp = await adminClient.PostAsJsonAsync("/api/v1/products", new CreateProductRequest(
            CategoryId: rootGpu.Id,
            Name: "ASUS TUF RTX 4060 Black",
            Sku: "SKU-GPU-ASUS-4060B",
            Brand: "ASUSTeK",
            Price: 8000000m,
            OriginalPrice: null,
            StockQuantity: 20,
            ImageUrl: "https://example.com/p6.jpg",
            AdditionalImages: null,
            ComponentType: ComponentType.Gpu,
            Specifications: CreateSampleGpuSpec()
        ));
        p6Resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var p6 = (await p6Resp.Content.ReadFromJsonAsync<ProductDto>(JsonOptions))!;

        // P7: CPU Root, Intel, 5M, Stock 0, Inactive (updated via PUT)
        using var p7CreateResp = await adminClient.PostAsJsonAsync("/api/v1/products", new CreateProductRequest(
            CategoryId: rootCpu.Id,
            Name: "Intel Core i3-12100 Special",
            Sku: "SKU-CPU-INTEL-12100",
            Brand: "Intel",
            Price: 5000000m,
            OriginalPrice: null,
            StockQuantity: 0,
            ImageUrl: "https://example.com/p7.jpg",
            AdditionalImages: null,
            ComponentType: ComponentType.Cpu,
            Specifications: CreateSampleCpuSpec(CpuSocket.LGA1700, 3.3, 4.3)
        ));
        p7CreateResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var p7Created = (await p7CreateResp.Content.ReadFromJsonAsync<ProductDto>(JsonOptions))!;

        using var p7UpdateResp = await staffClient.PutAsJsonAsync($"/api/v1/products/{p7Created.Id}", new UpdateProductRequest(
            CategoryId: p7Created.CategoryId,
            Name: p7Created.Name,
            Sku: p7Created.Sku,
            Brand: p7Created.Brand,
            Price: p7Created.Price,
            OriginalPrice: p7Created.OriginalPrice,
            StockQuantity: p7Created.StockQuantity,
            IsActive: false,
            ImageUrl: p7Created.ImageUrl,
            AdditionalImages: p7Created.AdditionalImages,
            ComponentType: p7Created.ComponentType,
            Specifications: p7Created.Specifications
        ));
        p7UpdateResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var p7 = (await p7UpdateResp.Content.ReadFromJsonAsync<ProductDto>(JsonOptions))!;

        return new StandardCatalogFixture(
            RootCpu: rootCpu,
            ChildCpu: childCpu,
            GrandchildCpu: grandchildCpu,
            SiblingCpu: siblingCpu,
            RootGpu: rootGpu,
            EmptyCpu: emptyCpu,
            P1: p1,
            P2: p2,
            P3: p3,
            P4: p4,
            P5: p5,
            P6: p6,
            P7: p7
        );
    }

    private static CpuSpecification CreateSampleCpuSpec(CpuSocket socket, double baseClock, double boostClock) =>
        new()
        {
            Socket = socket,
            CoreCount = 8,
            ThreadCount = 16,
            BaseClockGhz = baseClock,
            BoostClockGhz = boostClock,
            TdpWattage = 105,
            HasIntegratedGpu = true,
            SupportedMemoryType = RamType.DDR5,
            MaxMemorySpeedMhz = 5600
        };

    private static GpuSpecification CreateSampleGpuSpec() =>
        new()
        {
            Chipset = "RTX 4060",
            VramGb = 8,
            VramType = "GDDR6",
            LengthMm = 242,
            SlotWidth = 2.0,
            TdpWattage = 115,
            RecommendedPsuWattage = 550,
            Requires12VHPWR = false,
            PowerConnectors = "1x 8-pin"
        };

    private record StandardCatalogFixture(
        CategoryDto RootCpu,
        CategoryDto ChildCpu,
        CategoryDto GrandchildCpu,
        CategoryDto SiblingCpu,
        CategoryDto RootGpu,
        CategoryDto EmptyCpu,
        ProductDto P1,
        ProductDto P2,
        ProductDto P3,
        ProductDto P4,
        ProductDto P5,
        ProductDto P6,
        ProductDto P7
    );

    private record BusinessErrorResponse(string Code, string Description);
}

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Carts.Commands;
using RiuTek.Application.Test.Helpers;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;
using Xunit;

namespace RiuTek.Application.Test.Features.Carts;

public class AddPCBuildToCartUnitTests
{
    private static async Task<(User Customer, PCBuild Build, Product Cpu, Product Mobo, Product Ram)> SeedStandardBuildScenarioAsync(
        TestApplicationDbContext context,
        bool isCustomerActive = true,
        UserRole role = UserRole.Customer,
        int cpuStock = 10,
        int moboStock = 10,
        int ramStock = 10,
        bool isCpuActive = true,
        bool isMoboActive = true,
        bool isRamActive = true)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var customer = new User($"customer_{suffix}@riutek.test", "hash", $"Customer {suffix}", role)
        {
            IsActive = isCustomerActive
        };
        context.Users.Add(customer);

        var cat = new Category($"Cat {suffix}", $"cat-{suffix}", ComponentType.Cpu);
        context.Categories.Add(cat);

        var cpu = new Product(cat.Id, $"CPU {suffix}", $"cpu-{suffix}", $"SKU-CPU-{suffix}", "Intel", 5_000_000m, cpuStock, "cpu.png", ComponentType.Cpu, new CpuSpecification())
        {
            IsActive = isCpuActive
        };
        var mobo = new Product(cat.Id, $"Mobo {suffix}", $"mobo-{suffix}", $"SKU-MOBO-{suffix}", "MSI", 3_000_000m, moboStock, "mobo.png", ComponentType.Motherboard, new MotherboardSpecification())
        {
            IsActive = isMoboActive
        };
        var ram = new Product(cat.Id, $"RAM {suffix}", $"ram-{suffix}", $"SKU-RAM-{suffix}", "Kingston", 1_000_000m, ramStock, "ram.png", ComponentType.Ram, new RamSpecification())
        {
            IsActive = isRamActive
        };
        context.Products.AddRange(cpu, mobo, ram);

        var build = new PCBuild($"Build {suffix}", customer.Id, "Description");
        build.AddItem(new PCBuildItem(build.Id, cpu.Id, ComponentType.Cpu, cpu.Price, 1));
        build.AddItem(new PCBuildItem(build.Id, mobo.Id, ComponentType.Motherboard, mobo.Price, 1));
        build.AddItem(new PCBuildItem(build.Id, ram.Id, ComponentType.Ram, ram.Price, 2));
        context.PCBuilds.Add(build);

        await context.SaveChangesAsync();
        return (customer, build, cpu, mobo, ram);
    }

    private static Mock<ICurrentUserService> MockUser(Guid? userId, bool isAuthenticated = true)
    {
        var mock = new Mock<ICurrentUserService>();
        mock.Setup(x => x.IsAuthenticated).Returns(isAuthenticated);
        mock.Setup(x => x.UserId).Returns(userId);
        return mock;
    }

    [Fact]
    public async Task AddPCBuildToCart_WhenAnonymous_ReturnsUnauthorizedAndDoesNotMutate()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (_, build, _, _, _) = await SeedStandardBuildScenarioAsync(context);
        var authMock = MockUser(null, isAuthenticated: false);

        var handler = new AddPCBuildToCartCommandHandler(context, authMock.Object);
        var result = await handler.Handle(new AddPCBuildToCartCommand(build.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Unauthorized);
        result.Error.Code.Should().Be("Auth.Unauthorized");
        (await context.Carts.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AddPCBuildToCart_WhenInactiveCustomer_ReturnsForbidden()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (customer, build, _, _, _) = await SeedStandardBuildScenarioAsync(context, isCustomerActive: false);
        var authMock = MockUser(customer.Id);

        var handler = new AddPCBuildToCartCommandHandler(context, authMock.Object);
        var result = await handler.Handle(new AddPCBuildToCartCommand(build.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Forbidden);
        result.Error.Code.Should().Be("User.AccountInactive");
    }

    [Fact]
    public async Task AddPCBuildToCart_WhenAdminOrStaff_ReturnsForbidden()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (staff, build, _, _, _) = await SeedStandardBuildScenarioAsync(context, role: UserRole.Staff);
        var authMock = MockUser(staff.Id);

        var handler = new AddPCBuildToCartCommandHandler(context, authMock.Object);
        var result = await handler.Handle(new AddPCBuildToCartCommand(build.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Forbidden);
        result.Error.Code.Should().Be("PCBuild.Forbidden");
    }

    [Fact]
    public async Task AddPCBuildToCart_WhenBuildNotFound_ReturnsNotFound()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (customer, _, _, _, _) = await SeedStandardBuildScenarioAsync(context);
        var authMock = MockUser(customer.Id);

        var handler = new AddPCBuildToCartCommandHandler(context, authMock.Object);
        var result = await handler.Handle(new AddPCBuildToCartCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be("PCBuild.NotFound");
    }

    [Fact]
    public async Task AddPCBuildToCart_WhenBuildBelongsToOtherUser_ReturnsNotFound()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (_, build, _, _, _) = await SeedStandardBuildScenarioAsync(context);

        var otherCustomer = new User("other@test.com", "hash", "Other User", UserRole.Customer);
        context.Users.Add(otherCustomer);
        await context.SaveChangesAsync();

        var authMock = MockUser(otherCustomer.Id);

        var handler = new AddPCBuildToCartCommandHandler(context, authMock.Object);
        var result = await handler.Handle(new AddPCBuildToCartCommand(build.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be("PCBuild.NotFound");
    }

    [Fact]
    public async Task AddPCBuildToCart_WhenBuildHasNoItems_ReturnsConflict()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (customer, _, _, _, _) = await SeedStandardBuildScenarioAsync(context);

        var emptyBuild = new PCBuild("Empty Rig", customer.Id);
        context.PCBuilds.Add(emptyBuild);
        await context.SaveChangesAsync();

        var authMock = MockUser(customer.Id);
        var handler = new AddPCBuildToCartCommandHandler(context, authMock.Object);
        var result = await handler.Handle(new AddPCBuildToCartCommand(emptyBuild.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be("PCBuild.EmptyBuild");
    }

    [Fact]
    public async Task AddPCBuildToCart_WhenNoCartExists_CreatesCartAndAddsAllItems()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (customer, build, cpu, mobo, ram) = await SeedStandardBuildScenarioAsync(context);
        var authMock = MockUser(customer.Id);

        var handler = new AddPCBuildToCartCommandHandler(context, authMock.Object);
        var result = await handler.Handle(new AddPCBuildToCartCommand(build.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(3);
        result.Value.Items.Should().Contain(i => i.ProductId == cpu.Id && i.Quantity == 1);
        result.Value.Items.Should().Contain(i => i.ProductId == mobo.Id && i.Quantity == 1);
        result.Value.Items.Should().Contain(i => i.ProductId == ram.Id && i.Quantity == 2);

        // Verify in DB
        context.ClearTrackedChanges();
        var cartInDb = await context.Carts.Include(c => c.Items).FirstOrDefaultAsync(c => c.UserId == customer.Id);
        cartInDb.Should().NotBeNull();
        cartInDb!.Items.Should().HaveCount(3);
    }

    [Fact]
    public async Task AddPCBuildToCart_WhenCartExistsWithDifferentItems_MergesItems()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (customer, build, _, _, _) = await SeedStandardBuildScenarioAsync(context);

        var cat = await context.Categories.FirstAsync();
        var extraProduct = new Product(cat.Id, "Headset", "headset", "SKU-H", "Brand", 500_000m, 10, "h.png", ComponentType.Accessory, new AccessorySpecification());
        context.Products.Add(extraProduct);

        var existingCart = new Cart(customer.Id);
        existingCart.AddItem(extraProduct.Id, 1);
        context.Carts.Add(existingCart);
        await context.SaveChangesAsync();

        var authMock = MockUser(customer.Id);
        var handler = new AddPCBuildToCartCommandHandler(context, authMock.Object);
        var result = await handler.Handle(new AddPCBuildToCartCommand(build.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(4);
        result.Value.Items.Should().Contain(i => i.ProductId == extraProduct.Id && i.Quantity == 1);
    }

    [Fact]
    public async Task AddPCBuildToCart_WhenCartExistsWithSameProduct_AddsQuantitiesCorrectly()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (customer, build, _, _, ram) = await SeedStandardBuildScenarioAsync(context);

        var existingCart = new Cart(customer.Id);
        existingCart.AddItem(ram.Id, 3);
        context.Carts.Add(existingCart);
        await context.SaveChangesAsync();

        var authMock = MockUser(customer.Id);
        var handler = new AddPCBuildToCartCommandHandler(context, authMock.Object);
        var result = await handler.Handle(new AddPCBuildToCartCommand(build.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var ramItem = result.Value.Items.First(i => i.ProductId == ram.Id);
        ramItem.Quantity.Should().Be(5); // 3 existing + 2 from build
    }

    [Fact]
    public async Task AddPCBuildToCart_WhenMultipleItemsSuccess_PersistedVersionAndItemsMatchResponse()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (customer, build, _, _, _) = await SeedStandardBuildScenarioAsync(context);
        var authMock = MockUser(customer.Id);

        var handler = new AddPCBuildToCartCommandHandler(context, authMock.Object);
        var result = await handler.Handle(new AddPCBuildToCartCommand(build.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        context.ClearTrackedChanges();
        var reloadedCart = await context.Carts.Include(c => c.Items).FirstAsync(c => c.UserId == customer.Id);
        result.Value.Version.Should().Be(reloadedCart.Version);
        result.Value.Items.Count.Should().Be(reloadedCart.Items.Count);
    }

    [Fact]
    public async Task AddPCBuildToCart_WhenOneProductMissingInCatalog_FailsAtomically()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var customer = new User($"cust_{suffix}@riutek.test", "hash", "Customer", UserRole.Customer);
        context.Users.Add(customer);

        var build = new PCBuild("Build With Missing Product", customer.Id);
        build.AddItem(new PCBuildItem(build.Id, Guid.NewGuid(), ComponentType.Accessory, 50_000m, 1));
        context.PCBuilds.Add(build);
        await context.SaveChangesAsync();

        var authMock = MockUser(customer.Id);
        var handler = new AddPCBuildToCartCommandHandler(context, authMock.Object);
        var result = await handler.Handle(new AddPCBuildToCartCommand(build.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
        result.Error.Code.Should().Be("Cart.ProductNotFound");

        context.ClearTrackedChanges();
        (await context.Carts.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AddPCBuildToCart_WhenOneProductInactive_ReturnsConflictAndFailsAtomically()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (customer, build, _, mobo, _) = await SeedStandardBuildScenarioAsync(context);

        // Deactivate mobo
        mobo.IsActive = false;
        await context.SaveChangesAsync();

        var authMock = MockUser(customer.Id);
        var handler = new AddPCBuildToCartCommandHandler(context, authMock.Object);
        var result = await handler.Handle(new AddPCBuildToCartCommand(build.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be("Cart.ProductInactive");

        context.ClearTrackedChanges();
        (await context.Carts.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AddPCBuildToCart_WhenResultingQuantityExceedsStock_ReturnsConflictAndFailsAtomically()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (customer, build, _, _, ram) = await SeedStandardBuildScenarioAsync(context, ramStock: 4);

        // Pre-existing cart already has 3 RAMs. Build requests 2 RAMs (resulting: 5 > stock 4)
        var existingCart = new Cart(customer.Id);
        existingCart.AddItem(ram.Id, 3);
        context.Carts.Add(existingCart);
        await context.SaveChangesAsync();

        var authMock = MockUser(customer.Id);
        var handler = new AddPCBuildToCartCommandHandler(context, authMock.Object);
        var result = await handler.Handle(new AddPCBuildToCartCommand(build.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be("Cart.InsufficientStock");

        // Reload fresh context to verify atomicity
        context.ClearTrackedChanges();
        var reloaded = await context.Carts.Include(c => c.Items).FirstAsync(c => c.UserId == customer.Id);
        reloaded.Items.Should().HaveCount(1);
        reloaded.Items.First().Quantity.Should().Be(3);
    }

    [Fact]
    public async Task AddPCBuildToCart_WhenResultingQuantityExceeds99_ReturnsValidationAndFailsAtomically()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (customer, build, _, _, ram) = await SeedStandardBuildScenarioAsync(context, ramStock: 150);

        // Pre-existing cart has 98 RAMs. Build requests 2 RAMs (resulting: 100 > 99)
        var existingCart = new Cart(customer.Id);
        existingCart.AddItem(ram.Id, 98);
        context.Carts.Add(existingCart);
        await context.SaveChangesAsync();

        var authMock = MockUser(customer.Id);
        var handler = new AddPCBuildToCartCommandHandler(context, authMock.Object);
        var result = await handler.Handle(new AddPCBuildToCartCommand(build.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be("Cart.MaxQuantityExceeded");

        context.ClearTrackedChanges();
        var reloaded = await context.Carts.Include(c => c.Items).FirstAsync(c => c.UserId == customer.Id);
        reloaded.Items.First().Quantity.Should().Be(98);
    }

    [Fact]
    public async Task AddPCBuildToCart_WhenResultingDistinctProductsExceeds50_ReturnsValidationAndFailsAtomically()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (customer, build, _, _, _) = await SeedStandardBuildScenarioAsync(context);

        var cat = await context.Categories.FirstAsync();
        var existingCart = new Cart(customer.Id);

        // Add 49 distinct products to cart
        for (int i = 0; i < 49; i++)
        {
            var p = new Product(cat.Id, $"P{i}", $"p-{i}", $"SKU-{i}", "Brand", 10_000m, 50, "p.png", ComponentType.Accessory, new AccessorySpecification());
            context.Products.Add(p);
            existingCart.AddItem(p.Id, 1);
        }
        context.Carts.Add(existingCart);
        await context.SaveChangesAsync();

        // Build has 3 different products (CPU, Mobo, RAM) -> total 52 distinct products > 50
        var authMock = MockUser(customer.Id);
        var handler = new AddPCBuildToCartCommandHandler(context, authMock.Object);
        var result = await handler.Handle(new AddPCBuildToCartCommand(build.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be("Cart.MaxItemsExceeded");

        context.ClearTrackedChanges();
        var reloaded = await context.Carts.Include(c => c.Items).FirstAsync(c => c.UserId == customer.Id);
        reloaded.Items.Should().HaveCount(49);
    }

    [Fact]
    public async Task AddPCBuildToCart_Boundary99QuantityAnd50Distinct_SucceedsWhenStockSufficient()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var customer = new User($"boundary_{suffix}@riutek.test", "hash", "Boundary Customer", UserRole.Customer);
        context.Users.Add(customer);

        var cat = new Category($"Cat {suffix}", $"cat-{suffix}", ComponentType.Accessory);
        context.Categories.Add(cat);

        var existingCart = new Cart(customer.Id);

        // Pre-fill 49 distinct products
        for (int i = 0; i < 49; i++)
        {
            var p = new Product(cat.Id, $"Product {i}", $"p-{i}", $"SKU-{i}", "Brand", 10_000m, 50, "p.png", ComponentType.Accessory, new AccessorySpecification());
            context.Products.Add(p);
            existingCart.AddItem(p.Id, 1);
        }
        context.Carts.Add(existingCart);

        // Single 50th product with 99 quantity
        var product50 = new Product(cat.Id, "Product 50", "p-50", "SKU-50", "Brand", 10_000m, 100, "p.png", ComponentType.Accessory, new AccessorySpecification());
        context.Products.Add(product50);

        var build = new PCBuild("Single Item Build", customer.Id);
        build.AddItem(new PCBuildItem(build.Id, product50.Id, ComponentType.Accessory, product50.Price, 99));
        context.PCBuilds.Add(build);

        await context.SaveChangesAsync();

        var authMock = MockUser(customer.Id);
        var handler = new AddPCBuildToCartCommandHandler(context, authMock.Object);
        var result = await handler.Handle(new AddPCBuildToCartCommand(build.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(50);
        result.Value.Items.First(i => i.ProductId == product50.Id).Quantity.Should().Be(99);
    }

    [Fact]
    public async Task AddPCBuildToCart_WhenDbUpdateConcurrencyException_ReturnsConflict()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (customer, build, _, _, _) = await SeedStandardBuildScenarioAsync(context);
        var authMock = MockUser(customer.Id);

        context.SaveChangesInterceptor = () =>
            throw new DbUpdateConcurrencyException("Simulated concurrency conflict");

        var handler = new AddPCBuildToCartCommandHandler(context, authMock.Object);
        var result = await handler.Handle(new AddPCBuildToCartCommand(build.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be("Cart.ConcurrencyConflict");
    }

    [Fact]
    public async Task AddPCBuildToCart_WhenFirstCartUniqueRace_ReturnsConflict()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (customer, build, _, _, _) = await SeedStandardBuildScenarioAsync(context);
        var authMock = MockUser(customer.Id);

        context.SaveChangesInterceptor = () =>
            throw new DbUpdateException("Duplicate", new Exception("Unique constraint IX_Carts_UserId"));

        var handler = new AddPCBuildToCartCommandHandler(context, authMock.Object);
        var result = await handler.Handle(new AddPCBuildToCartCommand(build.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be("Cart.DuplicateCartCreation");
    }

    [Fact]
    public async Task AddPCBuildToCart_UsesLivePriceAndStockInResponseWithCorrectSubtotal()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (customer, build, cpu, mobo, ram) = await SeedStandardBuildScenarioAsync(context);

        // Change live product price in catalog
        var liveCpuPrice = 6_000_000m;
        cpu.Price = liveCpuPrice;
        await context.SaveChangesAsync();

        var authMock = MockUser(customer.Id);
        var handler = new AddPCBuildToCartCommandHandler(context, authMock.Object);
        var result = await handler.Handle(new AddPCBuildToCartCommand(build.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var cpuItem = result.Value.Items.First(i => i.ProductId == cpu.Id);
        cpuItem.UnitPrice.Should().Be(liveCpuPrice);

        var expectedSubtotal = (liveCpuPrice * 1) + (mobo.Price * 1) + (ram.Price * 2);
        result.Value.Subtotal.Should().Be(expectedSubtotal);
    }

    [Fact]
    public async Task AddPCBuildToCart_PCBuildStatusAndSnapshot_AreNotModified()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (customer, build, _, _, _) = await SeedStandardBuildScenarioAsync(context);
        var originalStatus = build.Status;
        var originalTotalPrice = build.TotalPrice;

        var authMock = MockUser(customer.Id);
        var handler = new AddPCBuildToCartCommandHandler(context, authMock.Object);
        var result = await handler.Handle(new AddPCBuildToCartCommand(build.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        context.ClearTrackedChanges();
        var reloadedBuild = await context.PCBuilds.FirstAsync(b => b.Id == build.Id);
        reloadedBuild.Status.Should().Be(originalStatus);
        reloadedBuild.TotalPrice.Should().Be(originalTotalPrice);
    }

    [Fact]
    public async Task AddPCBuildToCart_ProductStockAndStockMovement_AreNotModified()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (customer, build, cpu, mobo, ram) = await SeedStandardBuildScenarioAsync(context);
        var originalCpuStock = cpu.StockQuantity;
        var originalMoboStock = mobo.StockQuantity;
        var originalRamStock = ram.StockQuantity;

        var authMock = MockUser(customer.Id);
        var handler = new AddPCBuildToCartCommandHandler(context, authMock.Object);
        var result = await handler.Handle(new AddPCBuildToCartCommand(build.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        context.ClearTrackedChanges();
        var reloadedCpu = await context.Products.FirstAsync(p => p.Id == cpu.Id);
        var reloadedMobo = await context.Products.FirstAsync(p => p.Id == mobo.Id);
        var reloadedRam = await context.Products.FirstAsync(p => p.Id == ram.Id);

        reloadedCpu.StockQuantity.Should().Be(originalCpuStock);
        reloadedMobo.StockQuantity.Should().Be(originalMoboStock);
        reloadedRam.StockQuantity.Should().Be(originalRamStock);

        (await context.StockMovements.CountAsync()).Should().Be(0);
    }
}

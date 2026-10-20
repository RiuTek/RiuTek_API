using FluentAssertions;
using FluentValidation.TestHelper;
using Microsoft.EntityFrameworkCore;
using Moq;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Orders.Commands;
using RiuTek.Application.Features.Orders.Common;
using RiuTek.Application.Test.Helpers;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;
using Xunit;

namespace RiuTek.Application.Test.Features.Orders;

public class BuyNowPCBuildUnitTests
{
    private static async Task<(User User, UserAddress Address, PCBuild PCBuild, Product ProductCpu, Product ProductRam, Cart Cart)> SeedPrerequisitesAsync(
        TestApplicationDbContext context,
        bool isUserActive = true,
        UserRole userRole = UserRole.Customer,
        bool isProductActive = true,
        int cpuStock = 50,
        int ramStock = 50,
        decimal cpuCurrentPrice = 500_000m,
        decimal ramCurrentPrice = 300_000m,
        decimal cpuSnapshotPrice = 450_000m,
        decimal ramSnapshotPrice = 280_000m,
        bool isCompatible = true,
        int initialCartQuantity = 2)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User(
            email: $"buynow_build_{suffix}@riutek.test",
            passwordHash: "dummy_hash",
            fullName: $"Build Customer {suffix}",
            role: userRole,
            phoneNumber: "0901234567"
        )
        {
            IsActive = isUserActive
        };
        context.Users.Add(user);

        var address = new UserAddress(
            userId: user.Id,
            receiverName: $"Receiver {suffix}",
            phoneNumber: "0909999999",
            addressLine: "456 Build Street",
            ward: "Ward 2",
            district: "District 3",
            city: "Ho Chi Minh City",
            isDefault: true
        );
        context.UserAddresses.Add(address);

        var catCpu = new Category($"CPU Cat {suffix}", $"cpu-cat-{suffix}", ComponentType.Cpu);
        var catRam = new Category($"RAM Cat {suffix}", $"ram-cat-{suffix}", ComponentType.Ram);
        context.Categories.AddRange(catCpu, catRam);

        var productCpu = new Product(
            categoryId: catCpu.Id,
            name: $"Intel Core i7 {suffix}",
            slug: $"intel-i7-{suffix}",
            sku: $"SKU-CPU-{suffix}",
            brand: "Intel",
            price: cpuCurrentPrice,
            stockQuantity: cpuStock,
            imageUrl: "https://riutek.test/cpu.png",
            componentType: ComponentType.Cpu,
            specifications: new CpuSpecification { Socket = CpuSocket.LGA1700 }
        )
        {
            IsActive = isProductActive
        };

        var productRam = new Product(
            categoryId: catRam.Id,
            name: $"Corsair Vengeance 32GB {suffix}",
            slug: $"corsair-ram-{suffix}",
            sku: $"SKU-RAM-{suffix}",
            brand: "Corsair",
            price: ramCurrentPrice,
            stockQuantity: ramStock,
            imageUrl: "https://riutek.test/ram.png",
            componentType: ComponentType.Ram,
            specifications: new RamSpecification { MemoryType = RamType.DDR5, CapacityGb = 32 }
        )
        {
            IsActive = isProductActive
        };
        context.Products.AddRange(productCpu, productRam);

        var pcBuild = new PCBuild(
            name: $"Rig {suffix}",
            userId: user.Id,
            description: "Custom build for test"
        );
        if (!isCompatible)
        {
            pcBuild.ApplyCompatibilitySnapshot(false, 650, ["Incompatible CPU & RAM"]);
        }

        var itemCpu = new PCBuildItem(pcBuild.Id, productCpu.Id, ComponentType.Cpu, cpuSnapshotPrice, quantity: 1);
        var itemRam = new PCBuildItem(pcBuild.Id, productRam.Id, ComponentType.Ram, ramSnapshotPrice, quantity: 2);
        pcBuild.AddItem(itemCpu);
        pcBuild.AddItem(itemRam);
        context.PCBuilds.Add(pcBuild);

        // Pre-existing cart to verify Cart isolation
        var catAcc = new Category($"Acc Cat {suffix}", $"acc-cat-{suffix}", ComponentType.Accessory);
        context.Categories.Add(catAcc);
        var cartProduct = new Product(
            categoryId: catAcc.Id,
            name: $"Mouse {suffix}",
            slug: $"mouse-{suffix}",
            sku: $"SKU-MOUSE-{suffix}",
            brand: "Logitech",
            price: 100_000m,
            stockQuantity: 100,
            imageUrl: "https://riutek.test/mouse.png",
            componentType: ComponentType.Accessory,
            specifications: new AccessorySpecification()
        );
        context.Products.Add(cartProduct);

        var cart = new Cart(user.Id);
        cart.AddItem(cartProduct.Id, initialCartQuantity);
        context.Carts.Add(cart);

        await context.SaveChangesAsync();
        return (user, address, pcBuild, productCpu, productRam, cart);
    }

    private static Mock<ICurrentUserService> CreateCurrentUserMock(Guid? userId, bool isAuthenticated = true)
    {
        var mock = new Mock<ICurrentUserService>();
        mock.Setup(x => x.IsAuthenticated).Returns(isAuthenticated);
        mock.Setup(x => x.UserId).Returns(userId);
        return mock;
    }

    #region Validator Tests

    [Fact]
    public void Validator_WhenPCBuildIdEmpty_HasValidationError()
    {
        var validator = new BuyNowPCBuildCommandValidator();
        var command = new BuyNowPCBuildCommand(
            PCBuildId: Guid.Empty,
            AddressId: Guid.NewGuid(),
            PaymentMethod: PaymentMethod.COD,
            Notes: null,
            IdempotencyKey: "1234567890123456"
        );

        var result = validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.PCBuildId)
            .WithErrorMessage("PCBuildId is required.");
    }

    [Fact]
    public void Validator_WhenAddressIdEmpty_HasValidationError()
    {
        var validator = new BuyNowPCBuildCommandValidator();
        var command = new BuyNowPCBuildCommand(
            PCBuildId: Guid.NewGuid(),
            AddressId: Guid.Empty,
            PaymentMethod: PaymentMethod.COD,
            Notes: null,
            IdempotencyKey: "1234567890123456"
        );

        var result = validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.AddressId)
            .WithErrorMessage("AddressId is required.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("short-key-123")] // 13 chars
    public void Validator_WhenIdempotencyKeyMissingOrTooShort_HasValidationError(string key)
    {
        var validator = new BuyNowPCBuildCommandValidator();
        var command = new BuyNowPCBuildCommand(
            PCBuildId: Guid.NewGuid(),
            AddressId: Guid.NewGuid(),
            PaymentMethod: PaymentMethod.COD,
            Notes: null,
            IdempotencyKey: key
        );

        var result = validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.IdempotencyKey);
    }

    [Fact]
    public void Validator_WhenIdempotencyKeyTooLong_HasValidationError()
    {
        var validator = new BuyNowPCBuildCommandValidator();
        var command = new BuyNowPCBuildCommand(
            PCBuildId: Guid.NewGuid(),
            AddressId: Guid.NewGuid(),
            PaymentMethod: PaymentMethod.COD,
            Notes: null,
            IdempotencyKey: new string('A', 129)
        );

        var result = validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.IdempotencyKey)
            .WithErrorMessage("Idempotency-Key must be between 16 and 128 characters.");
    }

    [Fact]
    public void Validator_WhenNotesExceed1000Chars_HasValidationError()
    {
        var validator = new BuyNowPCBuildCommandValidator();
        var command = new BuyNowPCBuildCommand(
            PCBuildId: Guid.NewGuid(),
            AddressId: Guid.NewGuid(),
            PaymentMethod: PaymentMethod.COD,
            Notes: new string('N', 1001),
            IdempotencyKey: "1234567890123456"
        );

        var result = validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Notes)
            .WithErrorMessage("Notes cannot exceed 1000 characters.");
    }

    [Fact]
    public void Validator_WhenValidInput_PassesValidation()
    {
        var validator = new BuyNowPCBuildCommandValidator();
        var command = new BuyNowPCBuildCommand(
            PCBuildId: Guid.NewGuid(),
            AddressId: Guid.NewGuid(),
            PaymentMethod: PaymentMethod.COD,
            Notes: "Handle fragile PC parts",
            IdempotencyKey: "valid-idempotency-key-12345"
        );

        var result = validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    #endregion

    #region Handler Tests

    [Fact]
    public async Task Handle_WhenAnonymous_ReturnsUnauthorized()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, pcBuild, _, _, _) = await SeedPrerequisitesAsync(context);
        var authMock = CreateCurrentUserMock(null, isAuthenticated: false);

        var processor = new CheckoutOrderProcessor(context);
        var handler = new BuyNowPCBuildCommandHandler(context, authMock.Object, processor);

        var command = new BuyNowPCBuildCommand(pcBuild.Id, address.Id, PaymentMethod.COD, null, "1234567890123456");
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Auth.Unauthorized");
    }

    [Fact]
    public async Task Handle_WhenUserInactive_ReturnsForbidden()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, pcBuild, _, _, _) = await SeedPrerequisitesAsync(context, isUserActive: false);
        var authMock = CreateCurrentUserMock(user.Id);

        var processor = new CheckoutOrderProcessor(context);
        var handler = new BuyNowPCBuildCommandHandler(context, authMock.Object, processor);

        var command = new BuyNowPCBuildCommand(pcBuild.Id, address.Id, PaymentMethod.COD, null, "1234567890123456");
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("User.AccountInactive");
    }

    [Theory]
    [InlineData(UserRole.Staff)]
    [InlineData(UserRole.Admin)]
    public async Task Handle_WhenUserIsStaffOrAdmin_ReturnsForbidden(UserRole role)
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, pcBuild, _, _, _) = await SeedPrerequisitesAsync(context, userRole: role);
        var authMock = CreateCurrentUserMock(user.Id);

        var processor = new CheckoutOrderProcessor(context);
        var handler = new BuyNowPCBuildCommandHandler(context, authMock.Object, processor);

        var command = new BuyNowPCBuildCommand(pcBuild.Id, address.Id, PaymentMethod.COD, null, "1234567890123456");
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("PCBuild.Forbidden");
    }

    [Fact]
    public async Task Handle_WhenPCBuildNotFoundOrOwnedByOtherUser_ReturnsNotFound()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, pcBuild, _, _, _) = await SeedPrerequisitesAsync(context);
        var authMock = CreateCurrentUserMock(user.Id);

        var processor = new CheckoutOrderProcessor(context);
        var handler = new BuyNowPCBuildCommandHandler(context, authMock.Object, processor);

        var nonExistentBuildId = Guid.NewGuid();
        var command = new BuyNowPCBuildCommand(nonExistentBuildId, address.Id, PaymentMethod.COD, null, "1234567890123456");
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("PCBuild.NotFound");
    }

    [Fact]
    public async Task Handle_WhenPCBuildHasNoItems_ReturnsConflict()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, _, _, _, _) = await SeedPrerequisitesAsync(context);
        var emptyBuild = new PCBuild("Empty Build", user.Id, "No parts");
        context.PCBuilds.Add(emptyBuild);
        await context.SaveChangesAsync();

        var authMock = CreateCurrentUserMock(user.Id);
        var processor = new CheckoutOrderProcessor(context);
        var handler = new BuyNowPCBuildCommandHandler(context, authMock.Object, processor);

        var command = new BuyNowPCBuildCommand(emptyBuild.Id, address.Id, PaymentMethod.COD, null, "1234567890123456");
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("PCBuild.EmptyBuild");
    }

    [Fact]
    public async Task Handle_WhenAddressOwnedByOtherUser_ReturnsNotFound()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, _, pcBuild, _, _, _) = await SeedPrerequisitesAsync(context);

        // Address belonging to another user
        var otherUserId = Guid.NewGuid();
        var otherAddress = new UserAddress(
            userId: otherUserId,
            receiverName: "Other",
            phoneNumber: "0999999999",
            addressLine: "Other Line",
            ward: "Ward X",
            district: "District Y",
            city: "City Z"
        );
        context.UserAddresses.Add(otherAddress);
        await context.SaveChangesAsync();

        var authMock = CreateCurrentUserMock(user.Id);
        var processor = new CheckoutOrderProcessor(context);
        var handler = new BuyNowPCBuildCommandHandler(context, authMock.Object, processor);

        var command = new BuyNowPCBuildCommand(pcBuild.Id, otherAddress.Id, PaymentMethod.COD, null, "1234567890123456");
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Checkout.AddressNotFound");
    }

    [Fact]
    public async Task Handle_WhenProductNotFoundInDatabase_ReturnsNotFound()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, _, _, _, _) = await SeedPrerequisitesAsync(context);

        var ghostProductId = Guid.NewGuid();
        var ghostBuild = new PCBuild("Ghost Build", user.Id, "Build with missing product");
        var ghostItem = new PCBuildItem(ghostBuild.Id, ghostProductId, ComponentType.Gpu, 1_000_000m, 1);
        ghostBuild.AddItem(ghostItem);
        context.PCBuilds.Add(ghostBuild);
        await context.SaveChangesAsync();

        var authMock = CreateCurrentUserMock(user.Id);
        var processor = new CheckoutOrderProcessor(context);
        var handler = new BuyNowPCBuildCommandHandler(context, authMock.Object, processor);

        var command = new BuyNowPCBuildCommand(ghostBuild.Id, address.Id, PaymentMethod.COD, null, "1234567890123456");
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Checkout.ProductNotFound");
    }

    [Fact]
    public async Task Handle_HappyPath_UsesCurrentProductPriceNotSnapshot_SetsPCBuildIdOnAllItems_LeavesCartUntouched()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, pcBuild, productCpu, productRam, initialCart) = await SeedPrerequisitesAsync(
            context,
            cpuCurrentPrice: 500_000m,
            ramCurrentPrice: 300_000m,
            cpuSnapshotPrice: 400_000m, // snapshot is lower
            ramSnapshotPrice: 250_000m  // snapshot is lower
        );
        var initialCartVersion = initialCart.Version;

        var authMock = CreateCurrentUserMock(user.Id);
        var processor = new CheckoutOrderProcessor(context);
        var handler = new BuyNowPCBuildCommandHandler(context, authMock.Object, processor);

        var command = new BuyNowPCBuildCommand(pcBuild.Id, address.Id, PaymentMethod.COD, "Test COD Rig", "1234567890123456");
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value;
        dto.Status.Should().Be(OrderStatus.Confirmed);
        dto.PaymentMethod.Should().Be(PaymentMethod.COD);

        // Expected total: (1 * 500_000) + (2 * 300_000) = 1_100_000 (NOT snapshot: 400k + 500k = 900k)
        dto.TotalAmount.Should().Be(1_100_000m);
        dto.FinalAmount.Should().Be(1_100_000m);
        dto.Items.Should().HaveCount(2);

        // Verify items in DB have PCBuildId set
        var dbOrder = await context.Orders.Include(o => o.Items).FirstAsync(o => o.Id == dto.Id);
        dbOrder.Items.Should().AllSatisfy(i => i.PCBuildId.Should().Be(pcBuild.Id));

        // Verify stock decremented
        var dbCpu = await context.Products.FindAsync(productCpu.Id);
        var dbRam = await context.Products.FindAsync(productRam.Id);
        dbCpu!.StockQuantity.Should().Be(49);
        dbRam!.StockQuantity.Should().Be(48);

        // Verify Cart completely untouched
        var dbCart = await context.Carts.Include(c => c.Items).FirstAsync(c => c.UserId == user.Id);
        dbCart.Version.Should().Be(initialCartVersion);
        dbCart.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_WhenBuildIncompatibleOrIncomplete_StillAllowsCheckout()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, pcBuild, _, _, _) = await SeedPrerequisitesAsync(context, isCompatible: false);

        var authMock = CreateCurrentUserMock(user.Id);
        var processor = new CheckoutOrderProcessor(context);
        var handler = new BuyNowPCBuildCommandHandler(context, authMock.Object, processor);

        var command = new BuyNowPCBuildCommand(pcBuild.Id, address.Id, PaymentMethod.COD, null, "1234567890123456");
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenExistingOrderWithSameIdempotencyKey_ReplaysBeforeReadingBuildOrAddress()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, pcBuild, _, _, _) = await SeedPrerequisitesAsync(context);

        var authMock = CreateCurrentUserMock(user.Id);
        var processor = new CheckoutOrderProcessor(context);
        var handler = new BuyNowPCBuildCommandHandler(context, authMock.Object, processor);

        var idempotencyKey = "1234567890123456";
        var command1 = new BuyNowPCBuildCommand(pcBuild.Id, address.Id, PaymentMethod.COD, null, idempotencyKey);
        var result1 = await handler.Handle(command1, CancellationToken.None);
        result1.IsSuccess.Should().BeTrue();

        // Now delete the PCBuild to prove replay happens BEFORE reading PCBuild
        context.PCBuilds.Remove(pcBuild);
        await context.SaveChangesAsync();

        var command2 = new BuyNowPCBuildCommand(pcBuild.Id, address.Id, PaymentMethod.COD, null, idempotencyKey);
        var result2 = await handler.Handle(command2, CancellationToken.None);

        result2.IsSuccess.Should().BeTrue();
        result2.Value.Id.Should().Be(result1.Value.Id);
    }

    [Fact]
    public async Task Handle_WhenStripeDisabled_ReturnsValidationFailureBeforeMutation()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var (user, address, pcBuild, _, _, _) = await SeedPrerequisitesAsync(context);
        var authMock = CreateCurrentUserMock(user.Id);

        var stripeMock = new Mock<IStripePaymentGateway>();
        stripeMock.Setup(x => x.IsEnabled).Returns(false);

        var processor = new CheckoutOrderProcessor(context, stripeMock.Object);
        var handler = new BuyNowPCBuildCommandHandler(context, authMock.Object, processor);

        var command = new BuyNowPCBuildCommand(pcBuild.Id, address.Id, PaymentMethod.Stripe, null, "1234567890123456");
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Checkout.PaymentMethodNotAvailable");
        (await context.Orders.CountAsync()).Should().Be(0);
    }

    #endregion
}

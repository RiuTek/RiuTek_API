using FluentAssertions;
using Moq;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Features.Products.Commands;
using RiuTek.Application.Test.Helpers;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;
using RiuTek.Core.Enums;
using Xunit;

namespace RiuTek.Application.Test.Features.Products;

public class DeleteProductMediaCommandHandlerTests
{
    private const string ValidKey = "products/2026/10/01925b30-c9a7-7e61-a8d9-6012e1a3bc4d.jpg";
    private const string ValidUrl = "https://media.riutek.com/products/2026/10/01925b30-c9a7-7e61-a8d9-6012e1a3bc4d.jpg";

    private static async Task<User> SeedUserAsync(TestApplicationDbContext context, UserRole role = UserRole.Admin, bool isActive = true)
    {
        var user = new User($"user_{Guid.NewGuid():N}@riutek.test", "hash", "Staff User", role)
        {
            IsActive = isActive
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    private static async Task<Product> SeedProductAsync(
        TestApplicationDbContext context,
        string imageUrl = "https://media.riutek.com/products/2026/10/default.jpg",
        List<string>? additionalImages = null)
    {
        var category = new Category("CPU", "cpu", ComponentType.Cpu);
        context.Categories.Add(category);

        var product = new Product(
            category.Id,
            "Intel Core i9-14900K",
            "intel-core-i9-14900k",
            $"SKU-{Guid.NewGuid():N}",
            "Intel",
            599.99m,
            20,
            imageUrl,
            ComponentType.Cpu,
            ProductCommandValidatorTests.CreateValidCpuSpec())
        {
            AdditionalImages = additionalImages ?? []
        };

        context.Products.Add(product);
        await context.SaveChangesAsync();
        return product;
    }

    [Fact]
    public async Task Handle_WhenUserNotAuthenticated_ReturnsUnauthorized()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var storageMock = new Mock<IProductMediaStorage>();
        storageMock.Setup(s => s.IsEnabled).Returns(true);

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(false);

        var handler = new DeleteProductMediaCommandHandler(storageMock.Object, context, currentUserMock.Object);

        var result = await handler.Handle(new DeleteProductMediaCommand(ValidKey), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.Unauthorized");
    }

    [Fact]
    public async Task Handle_WhenUserIsCustomer_ReturnsForbidden()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var user = await SeedUserAsync(context, role: UserRole.Customer);

        var storageMock = new Mock<IProductMediaStorage>();
        storageMock.Setup(s => s.IsEnabled).Returns(true);

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(user.Id);
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Customer.ToString());

        var handler = new DeleteProductMediaCommandHandler(storageMock.Object, context, currentUserMock.Object);

        var result = await handler.Handle(new DeleteProductMediaCommand(ValidKey), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.Forbidden");
    }

    [Fact]
    public async Task Handle_WhenStorageDisabled_ReturnsUnavailable()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var user = await SeedUserAsync(context, role: UserRole.Admin);

        var storageMock = new Mock<IProductMediaStorage>();
        storageMock.Setup(s => s.IsEnabled).Returns(false);

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(user.Id);
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Admin.ToString());

        var handler = new DeleteProductMediaCommandHandler(storageMock.Object, context, currentUserMock.Object);

        var result = await handler.Handle(new DeleteProductMediaCommand(ValidKey), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("ProductMedia.StorageDisabled");
    }

    [Fact]
    public async Task Handle_WhenKeyInvalid_ReturnsValidation()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var user = await SeedUserAsync(context, role: UserRole.Admin);

        var storageMock = new Mock<IProductMediaStorage>();
        storageMock.Setup(s => s.IsEnabled).Returns(true);

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(user.Id);
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Admin.ToString());

        var handler = new DeleteProductMediaCommandHandler(storageMock.Object, context, currentUserMock.Object);

        var result = await handler.Handle(new DeleteProductMediaCommand("not-managed/key.jpg"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("ProductMedia.InvalidKey");
    }

    [Fact]
    public async Task Handle_WhenMediaInUseAsPrimaryImageUrl_ReturnsConflict()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var user = await SeedUserAsync(context, role: UserRole.Admin);
        await SeedProductAsync(context, imageUrl: ValidUrl);

        var storageMock = new Mock<IProductMediaStorage>();
        storageMock.Setup(s => s.IsEnabled).Returns(true);
        storageMock.Setup(s => s.GetPublicUrl(ValidKey)).Returns(ValidUrl);

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(user.Id);
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Admin.ToString());

        var handler = new DeleteProductMediaCommandHandler(storageMock.Object, context, currentUserMock.Object);

        var result = await handler.Handle(new DeleteProductMediaCommand(ValidKey), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("ProductMedia.InUse");
        storageMock.Verify(s => s.DeleteObjectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenMediaInUseInAdditionalImages_ReturnsConflict()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var user = await SeedUserAsync(context, role: UserRole.Admin);
        await SeedProductAsync(context, additionalImages: [ValidUrl]);

        var storageMock = new Mock<IProductMediaStorage>();
        storageMock.Setup(s => s.IsEnabled).Returns(true);
        storageMock.Setup(s => s.GetPublicUrl(ValidKey)).Returns(ValidUrl);

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(user.Id);
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Admin.ToString());

        var handler = new DeleteProductMediaCommandHandler(storageMock.Object, context, currentUserMock.Object);

        var result = await handler.Handle(new DeleteProductMediaCommand(ValidKey), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("ProductMedia.InUse");
        storageMock.Verify(s => s.DeleteObjectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenMediaUnreferenced_DeletesFromStorageAndReturnsSuccess()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var user = await SeedUserAsync(context, role: UserRole.Staff);
        await SeedProductAsync(context, imageUrl: "https://media.riutek.com/products/other.jpg");

        var storageMock = new Mock<IProductMediaStorage>();
        storageMock.Setup(s => s.IsEnabled).Returns(true);
        storageMock.Setup(s => s.GetPublicUrl(ValidKey)).Returns(ValidUrl);
        storageMock.Setup(s => s.DeleteObjectAsync(ValidKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(user.Id);
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Staff.ToString());

        var handler = new DeleteProductMediaCommandHandler(storageMock.Object, context, currentUserMock.Object);

        var result = await handler.Handle(new DeleteProductMediaCommand(ValidKey), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        storageMock.Verify(s => s.DeleteObjectAsync(ValidKey, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("products/invalid-format.jpg")]
    public void Validator_WhenObjectKeyInvalid_FailsValidation(string key)
    {
        var validator = new DeleteProductMediaCommandValidator();
        var result = validator.Validate(new DeleteProductMediaCommand(key));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_WhenObjectKeyValid_PassesValidation()
    {
        var validator = new DeleteProductMediaCommandValidator();
        var result = validator.Validate(new DeleteProductMediaCommand(ValidKey));

        result.IsValid.Should().BeTrue();
    }
}

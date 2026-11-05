using FluentAssertions;
using FluentValidation.TestHelper;
using Moq;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Banners.Commands;
using RiuTek.Application.Features.Banners.Common;
using RiuTek.Application.Features.Products.Commands;
using RiuTek.Application.Test.Helpers;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Test.Features.Banners;

public class BannerCommandTests
{
    private static TestApplicationDbContext CreateInMemoryContext()
    {
        return TestDbContextFactory.CreateInMemoryDbContext();
    }

    private static (Mock<ICurrentUserService> UserMock, User TestUser) SetupUser(
        TestApplicationDbContext context,
        UserRole role = UserRole.Admin,
        bool isActive = true,
        bool isAuthenticated = true)
    {
        var user = new User("user@example.com", "hash", "Admin User", role)
        {
            IsActive = isActive
        };
        context.Users.Add(user);
        context.SaveChanges();

        var userMock = new Mock<ICurrentUserService>();
        userMock.Setup(u => u.IsAuthenticated).Returns(isAuthenticated);
        userMock.Setup(u => u.UserId).Returns(isAuthenticated ? user.Id : (Guid?)null);
        userMock.Setup(u => u.UserRole).Returns(isAuthenticated ? role.ToString() : null);

        return (userMock, user);
    }

    #region 1. Validation Tests

    [Fact]
    public void CreateBannerCommandValidator_ValidatesRulesProperly()
    {
        var validator = new CreateBannerCommandValidator();
        var validKey = "products/2026/11/11111111-2222-3333-4444-555555555555.webp";

        // Empty Title
        validator.TestValidate(new CreateBannerCommand("", validKey, "/products"))
            .ShouldHaveValidationErrorFor(x => x.Title);

        // Long Title (>160)
        validator.TestValidate(new CreateBannerCommand(new string('a', 161), validKey, "/products"))
            .ShouldHaveValidationErrorFor(x => x.Title);

        // Empty ImageObjectKey
        validator.TestValidate(new CreateBannerCommand("Title", "", "/products"))
            .ShouldHaveValidationErrorFor(x => x.ImageObjectKey);

        // Invalid ImageObjectKey format
        validator.TestValidate(new CreateBannerCommand("Title", "invalid/key.jpg", "/products"))
            .ShouldHaveValidationErrorFor(x => x.ImageObjectKey);

        // Empty TargetUrl
        validator.TestValidate(new CreateBannerCommand("Title", validKey, ""))
            .ShouldHaveValidationErrorFor(x => x.TargetUrl);

        // Long TargetUrl (>2048)
        validator.TestValidate(new CreateBannerCommand("Title", validKey, "/" + new string('a', 2048)))
            .ShouldHaveValidationErrorFor(x => x.TargetUrl);

        // Negative DisplayOrder
        validator.TestValidate(new CreateBannerCommand("Title", validKey, "/products", -1))
            .ShouldHaveValidationErrorFor(x => x.DisplayOrder);

        // Zero DisplayOrder is valid
        validator.TestValidate(new CreateBannerCommand("Title", validKey, "/products", 0))
            .ShouldNotHaveValidationErrorFor(x => x.DisplayOrder);

        // Valid command
        validator.TestValidate(new CreateBannerCommand("Valid Banner", validKey, "/products/slug/abc?sort=price#specs", 1, true))
            .ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("/products")]
    [InlineData("/products/slug/gaming-pc")]
    [InlineData("/products?categoryId=11111111-2222-3333-4444-555555555555")]
    [InlineData("/posts/tin-tuc-cong-nghe#section-1")]
    [InlineData("/")]
    public void IsValidInternalTargetUrl_AcceptsValidInternalPaths(string url)
    {
        BannerHelper.IsValidInternalTargetUrl(url).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("//malicious.com/attack")]
    [InlineData("http://external.com")]
    [InlineData("https://external.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=")]
    [InlineData("/products\\evil")]
    [InlineData("/products\n/test")]
    [InlineData("/products\r/test")]
    [InlineData("/products\t/test")]
    [InlineData("products/no-leading-slash")]
    public void IsValidInternalTargetUrl_RejectsDangerousOrExternalOrMalformedPaths(string? url)
    {
        BannerHelper.IsValidInternalTargetUrl(url).Should().BeFalse();
    }

    [Fact]
    public void UpdateBannerCommandValidator_ValidatesRulesProperly()
    {
        var validator = new UpdateBannerCommandValidator();
        var validKey = "products/2026/11/11111111-2222-3333-4444-555555555555.png";

        // Empty Id
        validator.TestValidate(new UpdateBannerCommand(Guid.Empty, "Title", validKey, "/products", 0, true))
            .ShouldHaveValidationErrorFor(x => x.Id);

        // Valid command
        validator.TestValidate(new UpdateBannerCommand(Guid.NewGuid(), "Title", validKey, "/products", 0, true))
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void DeleteBannerCommandValidator_ValidatesRulesProperly()
    {
        var validator = new DeleteBannerCommandValidator();

        validator.TestValidate(new DeleteBannerCommand(Guid.Empty))
            .ShouldHaveValidationErrorFor(x => x.Id);

        validator.TestValidate(new DeleteBannerCommand(Guid.NewGuid()))
            .ShouldNotHaveAnyValidationErrors();
    }

    #endregion

    #region 2. Authorization Matrix Tests

    [Fact]
    public async Task CreateBanner_AnonymousUser_Returns401Unauthorized_AndDbUnchanged()
    {
        using var context = CreateInMemoryContext();
        var (userMock, _) = SetupUser(context, isAuthenticated: false);

        var storageMock = new Mock<IProductMediaStorage>();
        storageMock.Setup(s => s.IsEnabled).Returns(true);

        var handler = new CreateBannerCommandHandler(context, storageMock.Object, userMock.Object);
        var cmd = new CreateBannerCommand("Title", "products/2026/11/11111111-2222-3333-4444-555555555555.jpg", "/products");

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(ErrorType.Unauthorized);
        context.Banners.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateBanner_CustomerRole_Returns403Forbidden_AndDbUnchanged()
    {
        using var context = CreateInMemoryContext();
        var (userMock, _) = SetupUser(context, role: UserRole.Customer);

        var storageMock = new Mock<IProductMediaStorage>();
        storageMock.Setup(s => s.IsEnabled).Returns(true);

        var handler = new CreateBannerCommandHandler(context, storageMock.Object, userMock.Object);
        var cmd = new CreateBannerCommand("Title", "products/2026/11/11111111-2222-3333-4444-555555555555.jpg", "/products");

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(ErrorType.Forbidden);
        context.Banners.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateBanner_InactiveStaffAccount_Returns403Forbidden_AndDbUnchanged()
    {
        using var context = CreateInMemoryContext();
        var (userMock, _) = SetupUser(context, role: UserRole.Staff, isActive: false);

        var storageMock = new Mock<IProductMediaStorage>();
        storageMock.Setup(s => s.IsEnabled).Returns(true);

        var handler = new CreateBannerCommandHandler(context, storageMock.Object, userMock.Object);
        var cmd = new CreateBannerCommand("Title", "products/2026/11/11111111-2222-3333-4444-555555555555.jpg", "/products");

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(ErrorType.Forbidden);
        context.Banners.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateBanner_RevokedDbRole_Returns403Forbidden_AndDbUnchanged()
    {
        using var context = CreateInMemoryContext();
        // Claim says Admin, but DB entity role was revoked to Customer
        var (userMock, userEntity) = SetupUser(context, role: UserRole.Customer);
        userMock.Setup(u => u.UserRole).Returns("Admin");

        var storageMock = new Mock<IProductMediaStorage>();
        storageMock.Setup(s => s.IsEnabled).Returns(true);

        var handler = new CreateBannerCommandHandler(context, storageMock.Object, userMock.Object);
        var cmd = new CreateBannerCommand("Title", "products/2026/11/11111111-2222-3333-4444-555555555555.jpg", "/products");

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(ErrorType.Forbidden);
        context.Banners.Should().BeEmpty();
    }

    #endregion

    #region 3. Create Banner Handlers & Storage Tests

    [Fact]
    public async Task CreateBanner_StorageDisabled_Returns503_AndDbUnchanged()
    {
        using var context = CreateInMemoryContext();
        var (userMock, _) = SetupUser(context, role: UserRole.Staff);

        var storageMock = new Mock<IProductMediaStorage>();
        storageMock.Setup(s => s.IsEnabled).Returns(false);

        var handler = new CreateBannerCommandHandler(context, storageMock.Object, userMock.Object);
        var cmd = new CreateBannerCommand("Title", "products/2026/11/11111111-2222-3333-4444-555555555555.webp", "/products");

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(ErrorType.Unavailable);
        context.Banners.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateBanner_ObjectMetadataNotFound_Returns404_AndDbUnchanged()
    {
        using var context = CreateInMemoryContext();
        var (userMock, _) = SetupUser(context, role: UserRole.Staff);

        var key = "products/2026/11/11111111-2222-3333-4444-555555555555.webp";
        var storageMock = new Mock<IProductMediaStorage>();
        storageMock.Setup(s => s.IsEnabled).Returns(true);
        storageMock.Setup(s => s.GetObjectMetadataAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<ProductMediaMetadata>(Error.NotFound("ProductMedia.NotFound", "File not found.")));

        var handler = new CreateBannerCommandHandler(context, storageMock.Object, userMock.Object);
        var cmd = new CreateBannerCommand("Title", key, "/products");

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(ErrorType.NotFound);
        context.Banners.Should().BeEmpty();
    }

    [Theory]
    [InlineData("image/gif", 1024, "Mismatched MIME")]
    [InlineData("image/webp", 0, "Zero length")]
    [InlineData("image/webp", 10000000, "Exceeds max file size")]
    public async Task CreateBanner_InvalidMetadata_ReturnsValidationError_AndDbUnchanged(string contentType, long contentLength, string reason)
    {
        using var context = CreateInMemoryContext();
        var (userMock, _) = SetupUser(context, role: UserRole.Staff);

        var key = "products/2026/11/11111111-2222-3333-4444-555555555555.webp";
        var storageMock = new Mock<IProductMediaStorage>();
        storageMock.Setup(s => s.IsEnabled).Returns(true);
        storageMock.Setup(s => s.MaxFileSizeBytes).Returns(5 * 1024 * 1024);
        storageMock.Setup(s => s.GetObjectMetadataAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new ProductMediaMetadata(key, contentType, contentLength, "etag")));

        var handler = new CreateBannerCommandHandler(context, storageMock.Object, userMock.Object);
        var cmd = new CreateBannerCommand("Title", key, "/products");

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse(reason);
        result.Error.Type.Should().Be(ErrorType.Validation);
        context.Banners.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateBanner_ValidParameters_DerivesServerPublicUrl_AndPersistsSuccessfully()
    {
        using var context = CreateInMemoryContext();
        var (userMock, _) = SetupUser(context, role: UserRole.Staff);

        var key = "products/2026/11/11111111-2222-3333-4444-555555555555.webp";
        var expectedPublicUrl = "https://media.riutek.com/" + key;

        var storageMock = new Mock<IProductMediaStorage>();
        storageMock.Setup(s => s.IsEnabled).Returns(true);
        storageMock.Setup(s => s.MaxFileSizeBytes).Returns(5 * 1024 * 1024);
        storageMock.Setup(s => s.GetObjectMetadataAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new ProductMediaMetadata(key, "image/webp", 512 * 1024, "etag-1")));
        storageMock.Setup(s => s.GetPublicUrl(key)).Returns(expectedPublicUrl);

        var handler = new CreateBannerCommandHandler(context, storageMock.Object, userMock.Object);
        var cmd = new CreateBannerCommand("Khuyến Mãi Mùa Hè", key, "/products?sale=summer", 2, true);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Title.Should().Be("Khuyến Mãi Mùa Hè");
        result.Value.ImageObjectKey.Should().Be(key);
        result.Value.ImageUrl.Should().Be(expectedPublicUrl);
        result.Value.TargetUrl.Should().Be("/products?sale=summer");
        result.Value.DisplayOrder.Should().Be(2);
        result.Value.IsActive.Should().BeTrue();

        var savedBanner = context.Banners.Single(b => b.Id == result.Value.Id);
        savedBanner.Title.Should().Be("Khuyến Mãi Mùa Hè");
        savedBanner.ImageUrl.Should().Be(expectedPublicUrl);
    }

    #endregion

    #region 4. Update Banner Handlers & Storage Tests

    [Fact]
    public async Task UpdateBanner_NotFound_Returns404()
    {
        using var context = CreateInMemoryContext();
        var (userMock, _) = SetupUser(context, role: UserRole.Admin);

        var storageMock = new Mock<IProductMediaStorage>();
        var handler = new UpdateBannerCommandHandler(context, storageMock.Object, userMock.Object);

        var cmd = new UpdateBannerCommand(Guid.NewGuid(), "New Title", "products/2026/11/11111111-2222-3333-4444-555555555555.webp", "/products", 1, true);
        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task UpdateBanner_SameImage_DoesNotCallHead_SucceedsEvenIfStorageUnavailable()
    {
        using var context = CreateInMemoryContext();
        var (userMock, _) = SetupUser(context, role: UserRole.Admin);

        var key = "products/2026/11/11111111-2222-3333-4444-555555555555.webp";
        var existingBanner = new Banner("Old Title", key, "https://media.riutek.com/" + key, "/old-path", 0, false);
        context.Banners.Add(existingBanner);
        await context.SaveChangesAsync();

        var storageMock = new Mock<IProductMediaStorage>();
        storageMock.Setup(s => s.IsEnabled).Returns(false); // Storage disabled, but same key!

        var handler = new UpdateBannerCommandHandler(context, storageMock.Object, userMock.Object);
        var cmd = new UpdateBannerCommand(existingBanner.Id, "Updated Title", key, "/new-path", 5, true);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Title.Should().Be("Updated Title");
        result.Value.TargetUrl.Should().Be("/new-path");
        result.Value.DisplayOrder.Should().Be(5);
        result.Value.IsActive.Should().BeTrue();
        result.Value.ImageObjectKey.Should().Be(key);

        storageMock.Verify(s => s.GetObjectMetadataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateBanner_NewImageFailedHead_PreservesOriginalRecord()
    {
        using var context = CreateInMemoryContext();
        var (userMock, _) = SetupUser(context, role: UserRole.Admin);

        var oldKey = "products/2026/11/11111111-2222-3333-4444-555555555555.webp";
        var newKey = "products/2026/11/99999999-8888-7777-6666-555555555555.webp";
        var existingBanner = new Banner("Original Title", oldKey, "https://media.riutek.com/" + oldKey, "/orig-path", 1, true);
        context.Banners.Add(existingBanner);
        await context.SaveChangesAsync();

        var storageMock = new Mock<IProductMediaStorage>();
        storageMock.Setup(s => s.IsEnabled).Returns(true);
        storageMock.Setup(s => s.GetObjectMetadataAsync(newKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<ProductMediaMetadata>(Error.NotFound("ProductMedia.NotFound", "New file missing")));

        var handler = new UpdateBannerCommandHandler(context, storageMock.Object, userMock.Object);
        var cmd = new UpdateBannerCommand(existingBanner.Id, "Attempted Title", newKey, "/new-path", 10, false);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(ErrorType.NotFound);

        // Verify original record is unchanged
        var refreshed = context.Banners.Single(b => b.Id == existingBanner.Id);
        refreshed.Title.Should().Be("Original Title");
        refreshed.ImageObjectKey.Should().Be(oldKey);
        refreshed.DisplayOrder.Should().Be(1);
        refreshed.IsActive.Should().BeTrue();
    }

    #endregion

    #region 5. Delete Banner & InUse Guard Tests

    [Fact]
    public async Task DeleteBanner_HardDeletesRecord_WithoutCallingStorageDelete()
    {
        using var context = CreateInMemoryContext();
        var (userMock, _) = SetupUser(context, role: UserRole.Admin);

        var key = "products/2026/11/11111111-2222-3333-4444-555555555555.webp";
        var banner = new Banner("Title", key, "https://media.riutek.com/" + key, "/path", 0, true);
        context.Banners.Add(banner);
        await context.SaveChangesAsync();

        var handler = new DeleteBannerCommandHandler(context, userMock.Object);
        var result = await handler.Handle(new DeleteBannerCommand(banner.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        context.Banners.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteProductMedia_ActiveBannerInUse_Returns409Conflict_AndDoesNotCallStorageDelete()
    {
        using var context = CreateInMemoryContext();
        var (userMock, _) = SetupUser(context, role: UserRole.Admin);

        var key = "products/2026/11/11111111-2222-3333-4444-555555555555.webp";
        var publicUrl = "https://media.riutek.com/" + key;

        var banner = new Banner("Active Banner", key, publicUrl, "/path", 0, isActive: true);
        context.Banners.Add(banner);
        await context.SaveChangesAsync();

        var storageMock = new Mock<IProductMediaStorage>();
        storageMock.Setup(s => s.IsEnabled).Returns(true);
        storageMock.Setup(s => s.GetPublicUrl(key)).Returns(publicUrl);

        var handler = new DeleteProductMediaCommandHandler(storageMock.Object, context, userMock.Object);
        var result = await handler.Handle(new DeleteProductMediaCommand(key), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be("ProductMedia.InUse");
        result.Error.Description.Should().Contain("banner");

        storageMock.Verify(s => s.DeleteObjectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteProductMedia_InactiveBannerInUse_Returns409Conflict_AndDoesNotCallStorageDelete()
    {
        using var context = CreateInMemoryContext();
        var (userMock, _) = SetupUser(context, role: UserRole.Admin);

        var key = "products/2026/11/11111111-2222-3333-4444-555555555555.webp";
        var publicUrl = "https://media.riutek.com/" + key;

        // Inactive banner must still prevent media deletion per Section 6
        var banner = new Banner("Inactive Banner", key, publicUrl, "/path", 0, isActive: false);
        context.Banners.Add(banner);
        await context.SaveChangesAsync();

        var storageMock = new Mock<IProductMediaStorage>();
        storageMock.Setup(s => s.IsEnabled).Returns(true);
        storageMock.Setup(s => s.GetPublicUrl(key)).Returns(publicUrl);

        var handler = new DeleteProductMediaCommandHandler(storageMock.Object, context, userMock.Object);
        var result = await handler.Handle(new DeleteProductMediaCommand(key), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(ErrorType.Conflict);
        result.Error.Code.Should().Be("ProductMedia.InUse");

        storageMock.Verify(s => s.DeleteObjectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteProductMedia_NotReferencedByAnyProductOrBanner_CallsStorageDeleteSuccessfully()
    {
        using var context = CreateInMemoryContext();
        var (userMock, _) = SetupUser(context, role: UserRole.Admin);

        var key = "products/2026/11/11111111-2222-3333-4444-555555555555.webp";
        var publicUrl = "https://media.riutek.com/" + key;

        var storageMock = new Mock<IProductMediaStorage>();
        storageMock.Setup(s => s.IsEnabled).Returns(true);
        storageMock.Setup(s => s.GetPublicUrl(key)).Returns(publicUrl);
        storageMock.Setup(s => s.DeleteObjectAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var handler = new DeleteProductMediaCommandHandler(storageMock.Object, context, userMock.Object);
        var result = await handler.Handle(new DeleteProductMediaCommand(key), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        storageMock.Verify(s => s.DeleteObjectAsync(key, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion
}

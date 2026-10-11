using FluentAssertions;
using Moq;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Products.Commands;
using RiuTek.Application.Test.Helpers;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;
using RiuTek.Core.Enums;
using Xunit;

namespace RiuTek.Application.Test.Features.Products;

public class ConfirmProductMediaUploadCommandHandlerTests
{
    private const string ValidKey = "products/2026/10/01925b30-c9a7-7e61-a8d9-6012e1a3bc4d.jpg";

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

    [Fact]
    public async Task Handle_WhenUserNotAuthenticated_ReturnsUnauthorized()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var storageMock = new Mock<IProductMediaStorage>();
        storageMock.Setup(s => s.IsEnabled).Returns(true);

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(false);

        var handler = new ConfirmProductMediaUploadCommandHandler(storageMock.Object, context, currentUserMock.Object);

        var result = await handler.Handle(
            new ConfirmProductMediaUploadCommand(ValidKey),
            CancellationToken.None);

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

        var handler = new ConfirmProductMediaUploadCommandHandler(storageMock.Object, context, currentUserMock.Object);

        var result = await handler.Handle(
            new ConfirmProductMediaUploadCommand(ValidKey),
            CancellationToken.None);

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

        var handler = new ConfirmProductMediaUploadCommandHandler(storageMock.Object, context, currentUserMock.Object);

        var result = await handler.Handle(
            new ConfirmProductMediaUploadCommand(ValidKey),
            CancellationToken.None);

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

        var handler = new ConfirmProductMediaUploadCommandHandler(storageMock.Object, context, currentUserMock.Object);

        var result = await handler.Handle(
            new ConfirmProductMediaUploadCommand("invalid/path/test.exe"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("ProductMedia.InvalidKey");
    }

    [Fact]
    public async Task Handle_WhenObjectNotFound_ReturnsStorageError()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var user = await SeedUserAsync(context, role: UserRole.Admin);

        var storageMock = new Mock<IProductMediaStorage>();
        storageMock.Setup(s => s.IsEnabled).Returns(true);
        storageMock.Setup(s => s.GetObjectMetadataAsync(ValidKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<ProductMediaMetadata>(Error.NotFound("ProductMedia.NotFound", "Object not found.")));

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(user.Id);
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Admin.ToString());

        var handler = new ConfirmProductMediaUploadCommandHandler(storageMock.Object, context, currentUserMock.Object);

        var result = await handler.Handle(
            new ConfirmProductMediaUploadCommand(ValidKey),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("ProductMedia.NotFound");
    }

    [Fact]
    public async Task Handle_WhenMetadataMimeTypeMismatch_CleansUpAndReturnsValidation()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var user = await SeedUserAsync(context, role: UserRole.Admin);

        var storageMock = new Mock<IProductMediaStorage>();
        storageMock.Setup(s => s.IsEnabled).Returns(true);
        // ValidKey has .jpg extension, but metadata has image/png
        storageMock.Setup(s => s.GetObjectMetadataAsync(ValidKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new ProductMediaMetadata(ValidKey, "image/png", 1024, "\"etag-123\"")));
        storageMock.Setup(s => s.DeleteObjectAsync(ValidKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(user.Id);
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Admin.ToString());

        var handler = new ConfirmProductMediaUploadCommandHandler(storageMock.Object, context, currentUserMock.Object);

        var result = await handler.Handle(
            new ConfirmProductMediaUploadCommand(ValidKey),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("ProductMedia.InvalidMetadata");
        storageMock.Verify(s => s.DeleteObjectAsync(ValidKey, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenMetadataSizeExceedsLimit_CleansUpAndReturnsValidation()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var user = await SeedUserAsync(context, role: UserRole.Admin);

        var storageMock = new Mock<IProductMediaStorage>();
        storageMock.Setup(s => s.IsEnabled).Returns(true);
        // Size > 5 MiB (5242880)
        storageMock.Setup(s => s.GetObjectMetadataAsync(ValidKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new ProductMediaMetadata(ValidKey, "image/jpeg", 5242881, "\"etag-123\"")));
        storageMock.Setup(s => s.DeleteObjectAsync(ValidKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(user.Id);
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Admin.ToString());

        var handler = new ConfirmProductMediaUploadCommandHandler(storageMock.Object, context, currentUserMock.Object);

        var result = await handler.Handle(
            new ConfirmProductMediaUploadCommand(ValidKey),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("ProductMedia.InvalidMetadata");
        storageMock.Verify(s => s.DeleteObjectAsync(ValidKey, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenValid_ReturnsProductMediaDto()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var user = await SeedUserAsync(context, role: UserRole.Staff);

        var storageMock = new Mock<IProductMediaStorage>();
        storageMock.Setup(s => s.IsEnabled).Returns(true);
        storageMock.Setup(s => s.GetObjectMetadataAsync(ValidKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new ProductMediaMetadata(ValidKey, "image/jpeg", 2048, "\"etag-abc\"")));
        storageMock.Setup(s => s.GetPublicUrl(ValidKey))
            .Returns($"https://media.riutek.com/{ValidKey}");

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(user.Id);
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Staff.ToString());

        var handler = new ConfirmProductMediaUploadCommandHandler(storageMock.Object, context, currentUserMock.Object);

        var result = await handler.Handle(
            new ConfirmProductMediaUploadCommand(ValidKey),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ObjectKey.Should().Be(ValidKey);
        result.Value.PublicUrl.Should().Be($"https://media.riutek.com/{ValidKey}");
        result.Value.ContentType.Should().Be("image/jpeg");
        result.Value.ContentLength.Should().Be(2048);
        result.Value.ETag.Should().Be("\"etag-abc\"");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("products/invalid-format.jpg")]
    [InlineData("../products/2026/10/01925b30-c9a7-7e61-a8d9-6012e1a3bc4d.jpg")]
    public void Validator_WhenObjectKeyInvalid_FailsValidation(string key)
    {
        var validator = new ConfirmProductMediaUploadCommandValidator();
        var result = validator.Validate(new ConfirmProductMediaUploadCommand(key));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_WhenObjectKeyValid_PassesValidation()
    {
        var validator = new ConfirmProductMediaUploadCommandValidator();
        var result = validator.Validate(new ConfirmProductMediaUploadCommand(ValidKey));

        result.IsValid.Should().BeTrue();
    }
}

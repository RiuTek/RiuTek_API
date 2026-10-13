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

public class CreateProductMediaUploadCommandHandlerTests
{
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
        storageMock.Setup(s => s.MaxFileSizeBytes).Returns(5242880L);

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(false);

        var handler = new CreateProductMediaUploadCommandHandler(storageMock.Object, context, currentUserMock.Object);

        var result = await handler.Handle(
            new CreateProductMediaUploadCommand("test.jpg", "image/jpeg", 1024),
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
        storageMock.Setup(s => s.MaxFileSizeBytes).Returns(5242880L);

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(user.Id);
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Customer.ToString());

        var handler = new CreateProductMediaUploadCommandHandler(storageMock.Object, context, currentUserMock.Object);

        var result = await handler.Handle(
            new CreateProductMediaUploadCommand("test.jpg", "image/jpeg", 1024),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.Forbidden");
    }

    [Fact]
    public async Task Handle_WhenUserIsInactive_ReturnsForbidden()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var user = await SeedUserAsync(context, role: UserRole.Staff, isActive: false);

        var storageMock = new Mock<IProductMediaStorage>();
        storageMock.Setup(s => s.IsEnabled).Returns(true);
        storageMock.Setup(s => s.MaxFileSizeBytes).Returns(5242880L);

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(user.Id);
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Staff.ToString());

        var handler = new CreateProductMediaUploadCommandHandler(storageMock.Object, context, currentUserMock.Object);

        var result = await handler.Handle(
            new CreateProductMediaUploadCommand("test.jpg", "image/jpeg", 1024),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("User.AccountInactive");
    }

    [Fact]
    public async Task Handle_WhenStorageDisabled_ReturnsUnavailableBeforeSizeValidation()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var user = await SeedUserAsync(context, role: UserRole.Admin);

        var storageMock = new Mock<IProductMediaStorage>();
        storageMock.Setup(s => s.IsEnabled).Returns(false);
        storageMock.Setup(s => s.MaxFileSizeBytes).Returns(1048576L); // 1 MiB

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(user.Id);
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Admin.ToString());

        var handler = new CreateProductMediaUploadCommandHandler(storageMock.Object, context, currentUserMock.Object);

        // 2 MiB request against 1 MiB storage, but storage is disabled
        var result = await handler.Handle(
            new CreateProductMediaUploadCommand("test.jpg", "image/jpeg", 2097152),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("ProductMedia.StorageDisabled");
    }

    [Fact]
    public async Task Handle_WhenContentLengthExceedsStorageMaxFileSizeBytes_ReturnsFileTooLarge()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var user = await SeedUserAsync(context, role: UserRole.Admin);

        var storageMock = new Mock<IProductMediaStorage>();
        storageMock.Setup(s => s.IsEnabled).Returns(true);
        storageMock.Setup(s => s.MaxFileSizeBytes).Returns(1048576L); // Configured max 1 MiB

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(user.Id);
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Admin.ToString());

        var handler = new CreateProductMediaUploadCommandHandler(storageMock.Object, context, currentUserMock.Object);

        // 2 MiB request against 1 MiB configured storage
        var result = await handler.Handle(
            new CreateProductMediaUploadCommand("test.jpg", "image/jpeg", 2097152),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("ProductMedia.FileTooLarge");
        result.Error.Description.Should().Contain("1048576");
    }

    [Fact]
    public async Task Handle_WhenContentLengthWithinConfigured10MiBStorage_Succeeds()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var user = await SeedUserAsync(context, role: UserRole.Admin);

        var storageMock = new Mock<IProductMediaStorage>();
        storageMock.Setup(s => s.IsEnabled).Returns(true);
        storageMock.Setup(s => s.MaxFileSizeBytes).Returns(10485760L); // Configured max 10 MiB
        storageMock.Setup(s => s.CreatePresignedUploadUrlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, string ct, CancellationToken _) =>
                Result.Success(new ProductMediaUploadDto(
                    key,
                    $"https://upload.r2.test/{key}",
                    $"https://cdn.riutek.com/{key}",
                    DateTimeOffset.UtcNow.AddMinutes(10),
                    "PUT",
                    new Dictionary<string, string> { { "Content-Type", ct } })));

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(user.Id);
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Admin.ToString());

        var handler = new CreateProductMediaUploadCommandHandler(storageMock.Object, context, currentUserMock.Object);

        // 6 MiB request against 10 MiB configured storage
        var result = await handler.Handle(
            new CreateProductMediaUploadCommand("test.jpg", "image/jpeg", 6291456),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData("image/jpeg", ".jpg")]
    [InlineData("image/png", ".png")]
    [InlineData("image/webp", ".webp")]
    public async Task Handle_WhenValid_GeneratesManagedKeyWithCorrectExtension_AndReturnsPresignedDto(string mime, string expectedExt)
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var user = await SeedUserAsync(context, role: UserRole.Staff);

        string? generatedKey = null;
        var storageMock = new Mock<IProductMediaStorage>();
        storageMock.Setup(s => s.IsEnabled).Returns(true);
        storageMock.Setup(s => s.MaxFileSizeBytes).Returns(5242880L);
        storageMock.Setup(s => s.CreatePresignedUploadUrlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, string ct, CancellationToken _) =>
            {
                generatedKey = key;
                return Result.Success(new ProductMediaUploadDto(
                    key,
                    $"https://upload.r2.test/{key}",
                    $"https://cdn.riutek.com/{key}",
                    DateTimeOffset.UtcNow.AddMinutes(10),
                    "PUT",
                    new Dictionary<string, string> { { "Content-Type", ct } }));
            });

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(u => u.IsAuthenticated).Returns(true);
        currentUserMock.Setup(u => u.UserId).Returns(user.Id);
        currentUserMock.Setup(u => u.UserRole).Returns(UserRole.Staff.ToString());

        var handler = new CreateProductMediaUploadCommandHandler(storageMock.Object, context, currentUserMock.Object);

        var result = await handler.Handle(
            new CreateProductMediaUploadCommand("../../../dangerous_filename.exe", mime, 2048),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value.HttpMethod.Should().Be("PUT");
        result.Value.RequiredHeaders.Should().ContainKey("Content-Type");
        result.Value.RequiredHeaders["Content-Type"].Should().Be(mime);

        // Key must begin with products/ and end with expected extension, never contain client filename
        generatedKey.Should().NotBeNull();
        generatedKey!.Should().StartWith("products/");
        generatedKey.Should().EndWith(expectedExt);
        generatedKey.Should().NotContain("dangerous_filename");
        generatedKey.Should().NotContain("..");
    }

    [Fact]
    public void Validator_WhenInputValid_PassesValidation()
    {
        var validator = new CreateProductMediaUploadCommandValidator();
        var command = new CreateProductMediaUploadCommand("photo.png", "image/png", 1024 * 1024);
        var result = validator.Validate(command);
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("", "image/png", 100)]
    [InlineData("test.png", "application/pdf", 100)]
    [InlineData("test.png", "image/svg+xml", 100)]
    [InlineData("test.png", "image/gif", 100)]
    [InlineData("test.png", "image/png", 0)]
    [InlineData("test.png", "image/png", -10)]
    [InlineData("test.png", "image/png", 20971521)] // > 20 MiB safety ceiling
    public void Validator_WhenInputInvalid_FailsValidation(string filename, string mime, long length)
    {
        var validator = new CreateProductMediaUploadCommandValidator();
        var command = new CreateProductMediaUploadCommand(filename, mime, length);
        var result = validator.Validate(command);
        result.IsValid.Should().BeFalse();
    }
}

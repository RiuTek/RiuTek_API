using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Features.Users.Commands;
using RiuTek.Application.Test.Helpers;
using RiuTek.Core.Entities;

namespace RiuTek.Application.Test.Features.Users;

public class UpdateProfileCommandHandlerTests
{
    private static async Task<User> SeedUserAsync(TestApplicationDbContext context, bool isActive = true)
    {
        var user = new User($"user_{Guid.NewGuid():N}@riutek.test", "hash", "Original Name")
        {
            IsActive = isActive,
            PhoneNumber = "0901111111"
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task Handle_WhenUserNotAuthenticated_ReturnsUnauthorized()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(x => x.IsAuthenticated).Returns(false);

        var handler = new UpdateProfileCommandHandler(context, currentUserMock.Object);

        var result = await handler.Handle(new UpdateProfileCommand("New Name", "0902222222"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.Unauthorized");
    }

    [Fact]
    public async Task Handle_WhenUserNotFound_ReturnsNotFound()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(x => x.IsAuthenticated).Returns(true);
        currentUserMock.Setup(x => x.UserId).Returns(Guid.NewGuid());

        var handler = new UpdateProfileCommandHandler(context, currentUserMock.Object);

        var result = await handler.Handle(new UpdateProfileCommand("New Name", "0902222222"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("User.NotFound");
    }

    [Fact]
    public async Task Handle_WhenUserIsInactive_ReturnsForbidden()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var user = await SeedUserAsync(context, isActive: false);

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(x => x.IsAuthenticated).Returns(true);
        currentUserMock.Setup(x => x.UserId).Returns(user.Id);

        var handler = new UpdateProfileCommandHandler(context, currentUserMock.Object);

        var result = await handler.Handle(new UpdateProfileCommand("New Name", "0902222222"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("User.AccountInactive");
    }

    [Fact]
    public async Task Handle_WhenValid_UpdatesFieldsAndUpdatedAt_ReturnsUserDto()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var user = await SeedUserAsync(context);

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(x => x.IsAuthenticated).Returns(true);
        currentUserMock.Setup(x => x.UserId).Returns(user.Id);

        var handler = new UpdateProfileCommandHandler(context, currentUserMock.Object);

        var result = await handler.Handle(new UpdateProfileCommand("  Updated Full Name  ", "  0908888888  "), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.FullName.Should().Be("Updated Full Name");
        result.Value.PhoneNumber.Should().Be("0908888888");

        var updatedUser = await context.Users.FirstAsync(u => u.Id == user.Id);
        updatedUser.FullName.Should().Be("Updated Full Name");
        updatedUser.PhoneNumber.Should().Be("0908888888");
        updatedUser.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public void Validator_WhenValid_PassesValidation()
    {
        var validator = new UpdateProfileCommandValidator();
        var result = validator.Validate(new UpdateProfileCommand("Valid Name", "0901234567"));
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validator_WhenInvalid_FailsValidation()
    {
        var validator = new UpdateProfileCommandValidator();

        var emptyName = validator.Validate(new UpdateProfileCommand("", "0901234567"));
        emptyName.IsValid.Should().BeFalse();

        var longName = validator.Validate(new UpdateProfileCommand(new string('a', 151), "0901234567"));
        longName.IsValid.Should().BeFalse();

        var longPhone = validator.Validate(new UpdateProfileCommand("Name", new string('1', 21)));
        longPhone.IsValid.Should().BeFalse();
    }
}

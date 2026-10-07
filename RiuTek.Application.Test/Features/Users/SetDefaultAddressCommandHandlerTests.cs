using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Features.Users.Commands;
using RiuTek.Application.Test.Helpers;
using RiuTek.Core.Entities;

namespace RiuTek.Application.Test.Features.Users;

public class SetDefaultAddressCommandHandlerTests
{
    private static async Task<User> SeedUserAsync(TestApplicationDbContext context, bool isActive = true)
    {
        var user = new User($"user_{Guid.NewGuid():N}@riutek.test", "hash", "Test User")
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
        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(x => x.IsAuthenticated).Returns(false);

        var handler = new SetDefaultAddressCommandHandler(context, currentUserMock.Object);

        var result = await handler.Handle(new SetDefaultAddressCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.Unauthorized");
    }

    [Fact]
    public async Task Handle_WhenUserIsInactive_ReturnsForbidden()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var user = await SeedUserAsync(context, isActive: false);

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(x => x.IsAuthenticated).Returns(true);
        currentUserMock.Setup(x => x.UserId).Returns(user.Id);

        var handler = new SetDefaultAddressCommandHandler(context, currentUserMock.Object);

        var result = await handler.Handle(new SetDefaultAddressCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("User.AccountInactive");
    }

    [Fact]
    public async Task Handle_WhenTargetBelongsToAnotherUserOrNotFound_ReturnsNotFound()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var user1 = await SeedUserAsync(context);
        var user2 = await SeedUserAsync(context);

        var user2Address = new UserAddress(user2.Id, "User 2", "0902222222", "Line", "W", "D", "C", true);
        context.UserAddresses.Add(user2Address);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(x => x.IsAuthenticated).Returns(true);
        currentUserMock.Setup(x => x.UserId).Returns(user1.Id);

        var handler = new SetDefaultAddressCommandHandler(context, currentUserMock.Object);

        var result = await handler.Handle(new SetDefaultAddressCommand(user2Address.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("UserAddress.NotFound");
    }

    [Fact]
    public async Task Handle_WhenTargetAlreadyDefault_IsIdempotentAndDoesNotMutateDb()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var user = await SeedUserAsync(context);

        var address = new UserAddress(user.Id, "Receiver", "0901111111", "Line", "W", "D", "C", true)
        {
            UpdatedAt = null
        };
        context.UserAddresses.Add(address);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(x => x.IsAuthenticated).Returns(true);
        currentUserMock.Setup(x => x.UserId).Returns(user.Id);

        var handler = new SetDefaultAddressCommandHandler(context, currentUserMock.Object);

        var result = await handler.Handle(new SetDefaultAddressCommand(address.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var reloaded = await context.UserAddresses.FirstAsync(a => a.Id == address.Id);
        reloaded.IsDefault.Should().BeTrue();
        reloaded.UpdatedAt.Should().BeNull(); // Unchanged
    }

    [Fact]
    public async Task Handle_WhenValid_SetsTargetToDefault_UnsetsOldDefault_UpdatesOnlyChangedRows()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var user = await SeedUserAsync(context);

        var oldDefault = new UserAddress(user.Id, "Old Default", "0901111111", "Line 1", "W", "D", "C", true)
        {
            UpdatedAt = null
        };
        var target = new UserAddress(user.Id, "Target", "0902222222", "Line 2", "W", "D", "C", false)
        {
            UpdatedAt = null
        };
        var bystander = new UserAddress(user.Id, "Bystander", "0903333333", "Line 3", "W", "D", "C", false)
        {
            UpdatedAt = null
        };
        context.UserAddresses.AddRange(oldDefault, target, bystander);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(x => x.IsAuthenticated).Returns(true);
        currentUserMock.Setup(x => x.UserId).Returns(user.Id);

        var handler = new SetDefaultAddressCommandHandler(context, currentUserMock.Object);

        var result = await handler.Handle(new SetDefaultAddressCommand(target.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var reloadedOld = await context.UserAddresses.FirstAsync(a => a.Id == oldDefault.Id);
        var reloadedTarget = await context.UserAddresses.FirstAsync(a => a.Id == target.Id);
        var reloadedBystander = await context.UserAddresses.FirstAsync(a => a.Id == bystander.Id);

        reloadedTarget.IsDefault.Should().BeTrue();
        reloadedTarget.UpdatedAt.Should().NotBeNull();

        reloadedOld.IsDefault.Should().BeFalse();
        reloadedOld.UpdatedAt.Should().NotBeNull();

        reloadedBystander.IsDefault.Should().BeFalse();
        reloadedBystander.UpdatedAt.Should().BeNull(); // Bystander was false and stayed false, no change
    }

    [Fact]
    public void Validator_ChecksAddressIdNotEmpty()
    {
        var validator = new SetDefaultAddressCommandValidator();
        validator.Validate(new SetDefaultAddressCommand(Guid.Empty)).IsValid.Should().BeFalse();
        validator.Validate(new SetDefaultAddressCommand(Guid.NewGuid())).IsValid.Should().BeTrue();
    }
}

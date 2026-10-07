using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Features.Users.Commands;
using RiuTek.Application.Test.Helpers;
using RiuTek.Core.Entities;

namespace RiuTek.Application.Test.Features.Users;

public class DeleteUserAddressCommandHandlerTests
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

        var handler = new DeleteUserAddressCommandHandler(context, currentUserMock.Object);

        var result = await handler.Handle(new DeleteUserAddressCommand(Guid.NewGuid()), CancellationToken.None);

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

        var handler = new DeleteUserAddressCommandHandler(context, currentUserMock.Object);

        var result = await handler.Handle(new DeleteUserAddressCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("User.AccountInactive");
    }

    [Fact]
    public async Task Handle_WhenTargetBelongsToAnotherUserOrNotFound_ReturnsNotFoundAndDbUnchanged()
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

        var handler = new DeleteUserAddressCommandHandler(context, currentUserMock.Object);

        var result = await handler.Handle(new DeleteUserAddressCommand(user2Address.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("UserAddress.NotFound");

        // Verify user2 address still exists
        (await context.UserAddresses.AnyAsync(a => a.Id == user2Address.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenDeletingNonDefaultAddress_KeepsCurrentDefaultAddress()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var user = await SeedUserAsync(context);

        var defaultAddr = new UserAddress(user.Id, "Default", "0901111111", "Line 1", "W", "D", "C", true);
        var nonDefaultAddr = new UserAddress(user.Id, "NonDefault", "0902222222", "Line 2", "W", "D", "C", false);
        context.UserAddresses.AddRange(defaultAddr, nonDefaultAddr);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(x => x.IsAuthenticated).Returns(true);
        currentUserMock.Setup(x => x.UserId).Returns(user.Id);

        var handler = new DeleteUserAddressCommandHandler(context, currentUserMock.Object);

        var result = await handler.Handle(new DeleteUserAddressCommand(nonDefaultAddr.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var remaining = await context.UserAddresses.Where(a => a.UserId == user.Id).ToListAsync();
        remaining.Should().HaveCount(1);
        remaining[0].Id.Should().Be(defaultAddr.Id);
        remaining[0].IsDefault.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenDeletingDefaultAddress_PromotesFirstRemainingByCreatedAtThenId()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var user = await SeedUserAsync(context);

        var baseTime = DateTime.UtcNow;

        var defaultAddr = new UserAddress(user.Id, "Current Default", "0901111111", "Line 1", "W", "D", "C", true)
        {
            CreatedAt = baseTime
        };
        var olderRemaining = new UserAddress(user.Id, "Older Remaining", "0902222222", "Line 2", "W", "D", "C", false)
        {
            CreatedAt = baseTime.AddMinutes(2)
        };
        var newerRemaining = new UserAddress(user.Id, "Newer Remaining", "0903333333", "Line 3", "W", "D", "C", false)
        {
            CreatedAt = baseTime.AddMinutes(10)
        };
        context.UserAddresses.AddRange(defaultAddr, olderRemaining, newerRemaining);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(x => x.IsAuthenticated).Returns(true);
        currentUserMock.Setup(x => x.UserId).Returns(user.Id);

        var handler = new DeleteUserAddressCommandHandler(context, currentUserMock.Object);

        var result = await handler.Handle(new DeleteUserAddressCommand(defaultAddr.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var remaining = await context.UserAddresses.Where(a => a.UserId == user.Id).ToListAsync();
        remaining.Should().HaveCount(2);

        var promoted = remaining.First(a => a.Id == olderRemaining.Id);
        promoted.IsDefault.Should().BeTrue();
        promoted.UpdatedAt.Should().NotBeNull();

        var unpromoted = remaining.First(a => a.Id == newerRemaining.Id);
        unpromoted.IsDefault.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WhenDeletingLastAddress_ReturnsSuccessAndLeavesEmptyList()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var user = await SeedUserAsync(context);

        var lastAddr = new UserAddress(user.Id, "Last", "0901111111", "Line", "W", "D", "C", true);
        context.UserAddresses.Add(lastAddr);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(x => x.IsAuthenticated).Returns(true);
        currentUserMock.Setup(x => x.UserId).Returns(user.Id);

        var handler = new DeleteUserAddressCommandHandler(context, currentUserMock.Object);

        var result = await handler.Handle(new DeleteUserAddressCommand(lastAddr.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var remaining = await context.UserAddresses.Where(a => a.UserId == user.Id).ToListAsync();
        remaining.Should().BeEmpty();
    }

    [Fact]
    public void Validator_ChecksAddressIdNotEmpty()
    {
        var validator = new DeleteUserAddressCommandValidator();
        validator.Validate(new DeleteUserAddressCommand(Guid.Empty)).IsValid.Should().BeFalse();
        validator.Validate(new DeleteUserAddressCommand(Guid.NewGuid())).IsValid.Should().BeTrue();
    }
}

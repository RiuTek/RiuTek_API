using FluentAssertions;
using Moq;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Features.Users.Queries;
using RiuTek.Application.Test.Helpers;
using RiuTek.Core.Entities;

namespace RiuTek.Application.Test.Features.Users;

public class GetMyAddressesQueryHandlerTests
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

        var handler = new GetMyAddressesQueryHandler(context, currentUserMock.Object);

        var result = await handler.Handle(new GetMyAddressesQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.Unauthorized");
    }

    [Fact]
    public async Task Handle_WhenUserNotFoundInDb_ReturnsNotFound()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(x => x.IsAuthenticated).Returns(true);
        currentUserMock.Setup(x => x.UserId).Returns(Guid.NewGuid());

        var handler = new GetMyAddressesQueryHandler(context, currentUserMock.Object);

        var result = await handler.Handle(new GetMyAddressesQuery(), CancellationToken.None);

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

        var handler = new GetMyAddressesQueryHandler(context, currentUserMock.Object);

        var result = await handler.Handle(new GetMyAddressesQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("User.AccountInactive");
    }

    [Fact]
    public async Task Handle_WhenUserHasNoAddresses_ReturnsEmptyList()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var user = await SeedUserAsync(context);

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(x => x.IsAuthenticated).Returns(true);
        currentUserMock.Setup(x => x.UserId).Returns(user.Id);

        var handler = new GetMyAddressesQueryHandler(context, currentUserMock.Object);

        var result = await handler.Handle(new GetMyAddressesQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ReturnsOnlyCurrentUserAddresses_InDeterministicOrder()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var user1 = await SeedUserAsync(context);
        var user2 = await SeedUserAsync(context);

        var baseTime = DateTime.UtcNow;

        var addrOther = new UserAddress(user2.Id, "Other User", "0900000000", "Other Rd", "W", "D", "C", true);

        // Address 1: Non-default, created earlier
        var addr1 = new UserAddress(user1.Id, "User1 Early", "0901111111", "Early Rd", "W1", "D1", "C1", false)
        {
            CreatedAt = baseTime.AddMinutes(1)
        };

        // Address 2: Default, created later
        var addr2 = new UserAddress(user1.Id, "User1 Default", "0902222222", "Default Rd", "W2", "D2", "C2", true)
        {
            CreatedAt = baseTime.AddMinutes(5)
        };

        // Address 3: Non-default, created later
        var addr3 = new UserAddress(user1.Id, "User1 Late", "0903333333", "Late Rd", "W3", "D3", "C3", false)
        {
            CreatedAt = baseTime.AddMinutes(10)
        };

        // Address 4: Non-default, created same time as addr3, but different Id
        var addr4 = new UserAddress(user1.Id, "User1 SameTime", "0904444444", "SameTime Rd", "W4", "D4", "C4", false)
        {
            CreatedAt = baseTime.AddMinutes(10)
        };

        context.UserAddresses.AddRange(addrOther, addr1, addr2, addr3, addr4);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(x => x.IsAuthenticated).Returns(true);
        currentUserMock.Setup(x => x.UserId).Returns(user1.Id);

        var handler = new GetMyAddressesQueryHandler(context, currentUserMock.Object);

        var result = await handler.Handle(new GetMyAddressesQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var list = result.Value;
        list.Should().HaveCount(4);

        // Default address first
        list[0].Id.Should().Be(addr2.Id);
        list[0].IsDefault.Should().BeTrue();

        // Then addr1 (earlier CreatedAt)
        list[1].Id.Should().Be(addr1.Id);

        // Then addr3 and addr4 ordered by Id
        var expectedLastTwoIds = new[] { addr3, addr4 }.OrderBy(a => a.Id).Select(a => a.Id).ToList();
        list[2].Id.Should().Be(expectedLastTwoIds[0]);
        list[3].Id.Should().Be(expectedLastTwoIds[1]);

        // Does not include user2's address
        list.Should().NotContain(a => a.UserId == user2.Id);
    }
}

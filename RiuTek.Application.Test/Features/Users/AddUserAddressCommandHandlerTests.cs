using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Features.Users.Commands;
using RiuTek.Application.Test.Helpers;
using RiuTek.Core.Entities;

namespace RiuTek.Application.Test.Features.Users;

public class AddUserAddressCommandHandlerTests
{
    [Fact]
    public async Task Handle_WhenUserHasNoAddress_FirstAddressAutomaticallyBecomesDefault_EvenIfRequestIsDefaultFalse()
    {
        // Arrange
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var userId = Guid.NewGuid();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(x => x.IsAuthenticated).Returns(true);
        currentUserMock.Setup(x => x.UserId).Returns(userId);

        var handler = new AddUserAddressCommandHandler(context, currentUserMock.Object);

        var command = new AddUserAddressCommand(
            ReceiverName: "Receiver 1",
            PhoneNumber: "0901234567",
            AddressLine: "123 Street",
            Ward: "Ward 1",
            District: "District 1",
            City: "HCM",
            IsDefault: false // explicitly false
        );

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.IsDefault.Should().BeTrue();

        var savedAddress = await context.UserAddresses.FirstOrDefaultAsync(a => a.UserId == userId);
        savedAddress.Should().NotBeNull();
        savedAddress!.IsDefault.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenUserHasAddressesAndRequestIsDefaultTrue_UnsetsPreviousDefaultAddress()
    {
        // Arrange
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var userId = Guid.NewGuid();

        var existingAddress = new UserAddress(
            userId: userId,
            receiverName: "Existing Default",
            phoneNumber: "0901111111",
            addressLine: "Old St",
            ward: "Ward 1",
            district: "District 1",
            city: "HCM",
            isDefault: true
        );
        context.UserAddresses.Add(existingAddress);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(x => x.IsAuthenticated).Returns(true);
        currentUserMock.Setup(x => x.UserId).Returns(userId);

        var handler = new AddUserAddressCommandHandler(context, currentUserMock.Object);

        var command = new AddUserAddressCommand(
            ReceiverName: "New Default",
            PhoneNumber: "0902222222",
            AddressLine: "New St",
            Ward: "Ward 2",
            District: "District 2",
            City: "HN",
            IsDefault: true
        );

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.IsDefault.Should().BeTrue();

        var addresses = await context.UserAddresses.Where(a => a.UserId == userId).ToListAsync();
        addresses.Should().HaveCount(2);

        var oldAddress = addresses.First(a => a.Id == existingAddress.Id);
        oldAddress.IsDefault.Should().BeFalse();

        var newAddress = addresses.First(a => a.Id == result.Value.Id);
        newAddress.IsDefault.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenUserAddsDefaultAddress_DoesNotAffectOtherUsersAddresses()
    {
        // Arrange
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var user1Id = Guid.NewGuid();
        var user2Id = Guid.NewGuid();

        var user2DefaultAddress = new UserAddress(
            userId: user2Id,
            receiverName: "User 2 Default",
            phoneNumber: "0909999999",
            addressLine: "User 2 St",
            ward: "Ward X",
            district: "District Y",
            city: "DN",
            isDefault: true
        );
        context.UserAddresses.Add(user2DefaultAddress);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(x => x.IsAuthenticated).Returns(true);
        currentUserMock.Setup(x => x.UserId).Returns(user1Id);

        var handler = new AddUserAddressCommandHandler(context, currentUserMock.Object);

        var command = new AddUserAddressCommand(
            ReceiverName: "User 1 Default",
            PhoneNumber: "0901234567",
            AddressLine: "User 1 St",
            Ward: "Ward 1",
            District: "District 1",
            City: "HCM",
            IsDefault: true
        );

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();

        // User 2's default address should remain true
        var reloadedUser2Address = await context.UserAddresses.FirstOrDefaultAsync(a => a.Id == user2DefaultAddress.Id);
        reloadedUser2Address!.IsDefault.Should().BeTrue();
    }
}

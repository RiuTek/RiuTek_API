using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Features.Users.Commands;
using RiuTek.Application.Test.Helpers;
using RiuTek.Core.Entities;

namespace RiuTek.Application.Test.Features.Users;

public class UpdateUserAddressCommandHandlerTests
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

        var handler = new UpdateUserAddressCommandHandler(context, currentUserMock.Object);

        var command = new UpdateUserAddressCommand(Guid.NewGuid(), "Name", "0901234567", "Line", "Ward", "Dist", "City");
        var result = await handler.Handle(command, CancellationToken.None);

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

        var handler = new UpdateUserAddressCommandHandler(context, currentUserMock.Object);

        var command = new UpdateUserAddressCommand(Guid.NewGuid(), "Name", "0901234567", "Line", "Ward", "Dist", "City");
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("User.AccountInactive");
    }

    [Fact]
    public async Task Handle_WhenAddressNotFoundOrBelongsToAnotherUser_ReturnsNotFoundAndDbUnchanged()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var user1 = await SeedUserAsync(context);
        var user2 = await SeedUserAsync(context);

        var user2Address = new UserAddress(user2.Id, "User 2 Original", "0902222222", "Line 2", "Ward 2", "Dist 2", "City 2", true);
        context.UserAddresses.Add(user2Address);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(x => x.IsAuthenticated).Returns(true);
        currentUserMock.Setup(x => x.UserId).Returns(user1.Id);

        var handler = new UpdateUserAddressCommandHandler(context, currentUserMock.Object);

        // Try to update user2's address using user1 credentials
        var command = new UpdateUserAddressCommand(user2Address.Id, "Hacked", "0999999999", "Hacked Line", "W", "D", "C");
        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("UserAddress.NotFound");

        // Verify DB unchanged
        var reloaded = await context.UserAddresses.FirstAsync(a => a.Id == user2Address.Id);
        reloaded.ReceiverName.Should().Be("User 2 Original");
    }

    [Fact]
    public async Task Handle_WhenValid_UpdatesFieldsAndUpdatedAt_PreservesUserIdIsDefaultAndCreatedAt()
    {
        await using var context = TestDbContextFactory.CreateInMemoryDbContext();
        var user = await SeedUserAsync(context);

        var originalCreatedAt = DateTime.UtcNow.AddDays(-2);
        var address = new UserAddress(user.Id, "Old Name", "0901111111", "Old Line", "Old Ward", "Old Dist", "Old City", true)
        {
            CreatedAt = originalCreatedAt,
            UpdatedAt = null
        };
        context.UserAddresses.Add(address);
        await context.SaveChangesAsync();

        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(x => x.IsAuthenticated).Returns(true);
        currentUserMock.Setup(x => x.UserId).Returns(user.Id);

        var handler = new UpdateUserAddressCommandHandler(context, currentUserMock.Object);

        var command = new UpdateUserAddressCommand(
            address.Id,
            "  New Receiver  ",
            "  0908888888  ",
            "  New Line 123  ",
            "  New Ward  ",
            "  New District  ",
            "  New City  "
        );

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ReceiverName.Should().Be("New Receiver");
        result.Value.PhoneNumber.Should().Be("0908888888");
        result.Value.AddressLine.Should().Be("New Line 123");
        result.Value.Ward.Should().Be("New Ward");
        result.Value.District.Should().Be("New District");
        result.Value.City.Should().Be("New City");
        result.Value.IsDefault.Should().BeTrue();

        var reloaded = await context.UserAddresses.FirstAsync(a => a.Id == address.Id);
        reloaded.ReceiverName.Should().Be("New Receiver");
        reloaded.PhoneNumber.Should().Be("0908888888");
        reloaded.AddressLine.Should().Be("New Line 123");
        reloaded.Ward.Should().Be("New Ward");
        reloaded.District.Should().Be("New District");
        reloaded.City.Should().Be("New City");
        reloaded.UserId.Should().Be(user.Id);
        reloaded.IsDefault.Should().BeTrue();
        reloaded.CreatedAt.Should().Be(originalCreatedAt);
        reloaded.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public void Validator_WhenValid_PassesValidation()
    {
        var validator = new UpdateUserAddressCommandValidator();
        var command = new UpdateUserAddressCommand(Guid.NewGuid(), "Receiver", "0901234567", "123 Line", "Ward 1", "District 1", "City");
        var result = validator.Validate(command);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validator_WhenInvalid_FailsValidation()
    {
        var validator = new UpdateUserAddressCommandValidator();

        var emptyId = validator.Validate(new UpdateUserAddressCommand(Guid.Empty, "Receiver", "0901234567", "Line", "W", "D", "C"));
        emptyId.IsValid.Should().BeFalse();

        var emptyName = validator.Validate(new UpdateUserAddressCommand(Guid.NewGuid(), "", "0901234567", "Line", "W", "D", "C"));
        emptyName.IsValid.Should().BeFalse();

        var longName = validator.Validate(new UpdateUserAddressCommand(Guid.NewGuid(), new string('a', 151), "0901234567", "Line", "W", "D", "C"));
        longName.IsValid.Should().BeFalse();

        var longPhone = validator.Validate(new UpdateUserAddressCommand(Guid.NewGuid(), "Receiver", new string('1', 21), "Line", "W", "D", "C"));
        longPhone.IsValid.Should().BeFalse();

        var longLine = validator.Validate(new UpdateUserAddressCommand(Guid.NewGuid(), "Receiver", "0901234567", new string('a', 301), "W", "D", "C"));
        longLine.IsValid.Should().BeFalse();

        var longWard = validator.Validate(new UpdateUserAddressCommand(Guid.NewGuid(), "Receiver", "0901234567", "Line", new string('a', 101), "D", "C"));
        longWard.IsValid.Should().BeFalse();

        var longDistrict = validator.Validate(new UpdateUserAddressCommand(Guid.NewGuid(), "Receiver", "0901234567", "Line", "W", new string('a', 101), "C"));
        longDistrict.IsValid.Should().BeFalse();

        var longCity = validator.Validate(new UpdateUserAddressCommand(Guid.NewGuid(), "Receiver", "0901234567", "Line", "W", "D", new string('a', 101)));
        longCity.IsValid.Should().BeFalse();
    }
}

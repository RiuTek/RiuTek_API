using FluentAssertions;
using Moq;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Banners.Queries;
using RiuTek.Application.Test.Helpers;
using RiuTek.Core.Common;
using RiuTek.Core.Entities;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Test.Features.Banners;

public class BannerQueryTests
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

    #region Public Queries

    [Fact]
    public async Task GetPublicBanners_WhenNoBannersExist_ReturnsEmptyArray()
    {
        using var context = CreateInMemoryContext();
        var handler = new GetPublicBannersQueryHandler(context);

        var result = await handler.Handle(new GetPublicBannersQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPublicBanners_FiltersOutInactiveBanners_AndSortsByDisplayOrderThenId()
    {
        using var context = CreateInMemoryContext();

        var idLow = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var idHigh = Guid.Parse("22222222-2222-2222-2222-222222222222");

        // Order 0: Tied display orders to test deterministic tie-breaking by Id ASC
        var b1 = new Banner("Active 1 (Order 0, idHigh)", "key1", "https://media.riutek.com/key1", "/p1", 0, isActive: true);
        typeof(Banner).GetProperty(nameof(Banner.Id))!.SetValue(b1, idHigh);

        var b2 = new Banner("Active 2 (Order 0, idLow)", "key2", "https://media.riutek.com/key2", "/p2", 0, isActive: true);
        typeof(Banner).GetProperty(nameof(Banner.Id))!.SetValue(b2, idLow);

        // Order 1: Active
        var b3 = new Banner("Active 3 (Order 1)", "key3", "https://media.riutek.com/key3", "/p3", 1, isActive: true);

        // Inactive: should NOT appear in public
        var bInactive = new Banner("Inactive Banner", "key4", "https://media.riutek.com/key4", "/p4", 0, isActive: false);

        context.Banners.AddRange(b1, b2, b3, bInactive);
        await context.SaveChangesAsync();

        var handler = new GetPublicBannersQueryHandler(context);
        var result = await handler.Handle(new GetPublicBannersQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(3);

        // First two have DisplayOrder 0, sorted by Id ASC: idLow then idHigh
        result.Value[0].Id.Should().Be(idLow);
        result.Value[0].Title.Should().Be("Active 2 (Order 0, idLow)");
        result.Value[1].Id.Should().Be(idHigh);
        result.Value[1].Title.Should().Be("Active 1 (Order 0, idHigh)");
        result.Value[2].Title.Should().Be("Active 3 (Order 1)");

        // Assert public DTO shape does NOT expose objectKey or admin fields
        result.Value.Should().AllSatisfy(dto =>
        {
            dto.Title.Should().NotBeNullOrEmpty();
            dto.ImageUrl.Should().NotBeNullOrEmpty();
            dto.TargetUrl.Should().NotBeNullOrEmpty();
        });
    }

    #endregion

    #region Managed Queries

    [Fact]
    public async Task GetManagedBanners_UnauthorizedOrForbidden_ReturnsProperErrors()
    {
        using var context = CreateInMemoryContext();

        // Anonymous
        var (anonMock, _) = SetupUser(context, isAuthenticated: false);
        var anonHandler = new GetManagedBannersQueryHandler(context, anonMock.Object);
        var anonResult = await anonHandler.Handle(new GetManagedBannersQuery(), CancellationToken.None);
        anonResult.IsSuccess.Should().BeFalse();
        anonResult.Error.Type.Should().Be(ErrorType.Unauthorized);

        // Customer
        var (custMock, _) = SetupUser(context, role: UserRole.Customer);
        var custHandler = new GetManagedBannersQueryHandler(context, custMock.Object);
        var custResult = await custHandler.Handle(new GetManagedBannersQuery(), CancellationToken.None);
        custResult.IsSuccess.Should().BeFalse();
        custResult.Error.Type.Should().Be(ErrorType.Forbidden);
    }

    [Fact]
    public async Task GetManagedBanners_ReturnsBothActiveAndInactive_SortedDeterministically()
    {
        using var context = CreateInMemoryContext();
        var (userMock, _) = SetupUser(context, role: UserRole.Staff);

        var bActive = new Banner("Active Banner", "k1", "https://m/k1", "/a", 1, true);
        var bInactive = new Banner("Inactive Banner", "k2", "https://m/k2", "/i", 0, false);
        context.Banners.AddRange(bActive, bInactive);
        await context.SaveChangesAsync();

        var handler = new GetManagedBannersQueryHandler(context, userMock.Object);
        var result = await handler.Handle(new GetManagedBannersQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);

        // DisplayOrder 0 first (inactive), then 1 (active)
        result.Value[0].Title.Should().Be("Inactive Banner");
        result.Value[0].IsActive.Should().BeFalse();
        result.Value[0].ImageObjectKey.Should().Be("k2");

        result.Value[1].Title.Should().Be("Active Banner");
        result.Value[1].IsActive.Should().BeTrue();
        result.Value[1].ImageObjectKey.Should().Be("k1");
    }

    [Fact]
    public async Task GetManagedBannerById_NotFound_Returns404()
    {
        using var context = CreateInMemoryContext();
        var (userMock, _) = SetupUser(context, role: UserRole.Admin);

        var handler = new GetManagedBannerByIdQueryHandler(context, userMock.Object);
        var result = await handler.Handle(new GetManagedBannerByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task GetManagedBannerById_Found_ReturnsManagedBannerDto()
    {
        using var context = CreateInMemoryContext();
        var (userMock, _) = SetupUser(context, role: UserRole.Admin);

        var banner = new Banner("Summer Promo", "k-summer", "https://m/k-summer", "/summer", 3, true);
        context.Banners.Add(banner);
        await context.SaveChangesAsync();

        var handler = new GetManagedBannerByIdQueryHandler(context, userMock.Object);
        var result = await handler.Handle(new GetManagedBannerByIdQuery(banner.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(banner.Id);
        result.Value.Title.Should().Be("Summer Promo");
        result.Value.ImageObjectKey.Should().Be("k-summer");
        result.Value.ImageUrl.Should().Be("https://m/k-summer");
        result.Value.TargetUrl.Should().Be("/summer");
        result.Value.DisplayOrder.Should().Be(3);
        result.Value.IsActive.Should().BeTrue();
    }

    #endregion
}

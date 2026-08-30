using FluentAssertions;
using RiuTek.Application.Common.Authorization;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Test.Authorization;

public class AuthorizationRulesTests
{
    [Theory]
    [InlineData("Admin")]
    [InlineData("admin")]
    [InlineData("ADMIN")]
    [InlineData("Staff")]
    [InlineData("staff")]
    [InlineData("STAFF")]
    public void IsContentManager_WhenRoleIsAdminOrStaff_ReturnsTrue(string role)
    {
        AuthorizationRules.IsContentManager(role).Should().BeTrue();
    }

    [Theory]
    [InlineData("Customer")]
    [InlineData("customer")]
    [InlineData("User")]
    [InlineData("Manager")]
    [InlineData("SuperAdmin")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void IsContentManager_WhenRoleIsNotAdminOrStaff_ReturnsFalse(string? role)
    {
        AuthorizationRules.IsContentManager(role).Should().BeFalse();
    }

    [Fact]
    public void IsContentManager_WithEnumToString_ReturnsExpected()
    {
        AuthorizationRules.IsContentManager(UserRole.Admin.ToString()).Should().BeTrue();
        AuthorizationRules.IsContentManager(UserRole.Staff.ToString()).Should().BeTrue();
        AuthorizationRules.IsContentManager(UserRole.Customer.ToString()).Should().BeFalse();
    }
}

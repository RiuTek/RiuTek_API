using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using RiuTek.Core.Constants;
using RiuTek.Core.Enums;

namespace RiuTek.Application.Test.Security;

public class AuthorizationPolicyTests
{
    private readonly IAuthorizationService _authorizationService;

    public AuthorizationPolicyTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization(options =>
        {
            options.AddPolicy(Policies.ContentManager, policy =>
                policy.RequireRole(UserRole.Admin.ToString(), UserRole.Staff.ToString()));
        });

        var serviceProvider = services.BuildServiceProvider();
        _authorizationService = serviceProvider.GetRequiredService<IAuthorizationService>();
    }

    [Theory]
    [InlineData(UserRole.Admin, true)]
    [InlineData(UserRole.Staff, true)]
    [InlineData(UserRole.Customer, false)]
    public async Task ContentManagerPolicy_ShouldAuthorizeCorrectRoles(UserRole role, bool expectedSuccess)
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, role.ToString())
        }, "TestAuth"));

        // Act
        var result = await _authorizationService.AuthorizeAsync(user, null, Policies.ContentManager);

        // Assert
        result.Succeeded.Should().Be(expectedSuccess);
    }
}

using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Core.Entities;
using RiuTek.Core.Enums;

namespace RiuTek.API.IntegrationTest.Infrastructure;

public static class IntegrationTestAuth
{
    public static string GenerateToken(RiuTekWebApplicationFactory factory, UserRole role)
    {
        using var scope = factory.Services.CreateScope();
        var jwtGenerator = scope.ServiceProvider.GetRequiredService<IJwtTokenGenerator>();

        var user = new User(
            email: $"test_{role.ToString().ToLowerInvariant()}@riutek.test",
            passwordHash: "dummy_hashed_password",
            fullName: $"{role} Test User",
            role: role
        );

        return jwtGenerator.GenerateAccessToken(user);
    }

    public static HttpClient CreateClientForRole(RiuTekWebApplicationFactory factory, UserRole role)
    {
        var token = GenerateToken(factory, role);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public static HttpClient CreateClientWithInvalidBearer(RiuTekWebApplicationFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid.corrupted.token.signature");
        return client;
    }
}

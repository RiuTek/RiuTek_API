using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RiuTek.API.Contracts;
using RiuTek.API.IntegrationTest.Infrastructure;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Core.Entities;
using RiuTek.Core.Entities.Specifications;
using RiuTek.Core.Enums;
using RiuTek.Infrastructure.Data;
using Xunit;

namespace RiuTek.API.IntegrationTest.Http;

[Collection(IntegrationTestCollection.Name)]
public class AuthIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainerFixture _fixture;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private const string CookieName = "riutek.refresh_token";

    public AuthIntegrationTests(PostgreSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await CleanupDataAsync();
    }

    public async Task DisposeAsync()
    {
        await CleanupDataAsync();
    }

    private async Task CleanupDataAsync()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            await db.CartItems.ExecuteDeleteAsync();
            await db.Carts.ExecuteDeleteAsync();
            await db.Products.ExecuteDeleteAsync();
            await db.Categories.ExecuteDeleteAsync();
            await db.Users.ExecuteDeleteAsync();
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    private HttpClient CreateTestClient()
    {
        return _fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false
        });
    }

    private static string? ExtractCookieValue(HttpResponseMessage response, string name)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            return null;
        }

        foreach (var cookie in values)
        {
            var tokenPart = cookie.Split(';')[0];
            var kvp = tokenPart.Split('=');
            if (kvp.Length >= 2 && kvp[0].Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                var rawValue = string.Join('=', kvp.Skip(1)).Trim();
                return WebUtility.UrlDecode(rawValue);
            }
        }

        return null;
    }

    private static string? GetRawCookieHeader(HttpResponseMessage response, string name)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            return null;
        }

        return values.FirstOrDefault(v => v.StartsWith($"{name}=", StringComparison.OrdinalIgnoreCase));
    }

    public record ErrorResponse(string? Code, string? Description);

    #region 1. Register Tests

    [Fact]
    public async Task Register_ValidRequest_CreatesCustomerWithNormalizedEmailHashedPasswordAndReturnsAccessResponseWithCookie()
    {
        // Arrange
        using var client = CreateTestClient();
        var request = new RegisterRequest(
            FullName: "Nguyễn Văn Test",
            Email: "MixedCase.User@Example.Com",
            Password: "SecurePassword123!",
            PhoneNumber: "0901234567"
        );

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", request);

        // Assert HTTP Status
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        // Assert JSON body does NOT contain refreshToken
        var json = await response.Content.ReadAsStringAsync();
        json.Should().NotContainEquivalentOf("refreshToken");

        var authResponse = JsonSerializer.Deserialize<AuthResponse>(json, JsonOptions);
        authResponse.Should().NotBeNull();
        authResponse!.AccessToken.Should().NotBeNullOrWhiteSpace();
        authResponse.ExpiresInSeconds.Should().BeGreaterThan(0);
        authResponse.User.Should().NotBeNull();
        authResponse.User.Email.Should().Be("mixedcase.user@example.com");
        authResponse.User.FullName.Should().Be("Nguyễn Văn Test");
        authResponse.User.Role.Should().Be(UserRole.Customer);

        // Assert Cookie attributes
        var rawCookie = GetRawCookieHeader(response, CookieName);
        rawCookie.Should().NotBeNull();
        rawCookie!.ToLowerInvariant().Should().Contain("httponly");
        rawCookie.ToLowerInvariant().Should().Contain("path=/api/v1/auth");
        rawCookie.ToLowerInvariant().Should().Contain("samesite=strict");

        var rawRefreshToken = ExtractCookieValue(response, CookieName);
        rawRefreshToken.Should().NotBeNullOrWhiteSpace();

        // Assert Database State
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IRefreshTokenHasher>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == "mixedcase.user@example.com");
        user.Should().NotBeNull();
        user!.Role.Should().Be(UserRole.Customer);
        passwordHasher.VerifyPassword("SecurePassword123!", user.PasswordHash).Should().BeTrue();

        // Database stores SHA-256 hash, NOT raw token
        user.RefreshToken.Should().NotBe(rawRefreshToken);
        user.RefreshToken.Should().Be(hasher.HashToken(rawRefreshToken!));
        user.RefreshToken!.Length.Should().Be(64);
        user.RefreshTokenExpiryTime.Should().BeAfter(DateTime.UtcNow.AddDays(6));
    }

    [Fact]
    public async Task Register_DuplicateEmail_ShouldReturnConflict409()
    {
        // Arrange
        using var client = CreateTestClient();
        var request = new RegisterRequest(
            FullName: "Existing User",
            Email: "duplicate@example.com",
            Password: "SecurePassword123!",
            PhoneNumber: null
        );

        var firstResponse = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        // Act: Try registering with duplicate email (different case)
        var duplicateRequest = request with { Email = "DUPLICATE@example.com" };
        var secondResponse = await client.PostAsJsonAsync("/api/v1/auth/register", duplicateRequest);

        // Assert
        secondResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var error = await secondResponse.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);
        error.Should().NotBeNull();
        error!.Code.Should().Be("Auth.EmailExists");
    }

    #endregion

    #region 2. Login Tests

    [Fact]
    public async Task Login_ValidCredentials_ShouldReturn200WithAccessTokenAndCookie_NoRefreshTokenInBody()
    {
        // Arrange
        using var client = CreateTestClient();
        var registerRequest = new RegisterRequest(
            FullName: "Login User",
            Email: "login_valid@example.com",
            Password: "SecurePassword123!",
            PhoneNumber: null
        );
        var regResponse = await client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
        regResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var loginRequest = new LoginRequest("login_valid@example.com", "SecurePassword123!");

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", loginRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        json.Should().NotContainEquivalentOf("refreshToken");

        var authResponse = JsonSerializer.Deserialize<AuthResponse>(json, JsonOptions);
        authResponse.Should().NotBeNull();
        authResponse!.AccessToken.Should().NotBeNullOrWhiteSpace();
        authResponse.ExpiresInSeconds.Should().BeGreaterThan(0);
        authResponse.User.Email.Should().Be("login_valid@example.com");

        var rawCookie = GetRawCookieHeader(response, CookieName);
        rawCookie.Should().NotBeNull();
        rawCookie!.ToLowerInvariant().Should().Contain("httponly");
        rawCookie.ToLowerInvariant().Should().Contain("path=/api/v1/auth");
        rawCookie.ToLowerInvariant().Should().Contain("samesite=strict");
    }

    [Fact]
    public async Task Login_InvalidCredentials_ShouldReturn401WithSameErrorCodeForWrongEmailAndWrongPassword()
    {
        // Arrange
        using var client = CreateTestClient();
        var registerRequest = new RegisterRequest(
            FullName: "Creds User",
            Email: "creds_test@example.com",
            Password: "CorrectPassword123!",
            PhoneNumber: null
        );
        await client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);

        // Act 1: Wrong Email
        var wrongEmailResponse = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest("non_existent@example.com", "CorrectPassword123!"));

        // Act 2: Wrong Password
        var wrongPasswordResponse = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest("creds_test@example.com", "WrongPassword999!"));

        // Assert 1
        wrongEmailResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var errorEmail = await wrongEmailResponse.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);
        errorEmail.Should().NotBeNull();
        errorEmail!.Code.Should().Be("Auth.InvalidCredentials");

        // Assert 2
        wrongPasswordResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var errorPassword = await wrongPasswordResponse.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);
        errorPassword.Should().NotBeNull();
        errorPassword!.Code.Should().Be("Auth.InvalidCredentials");
    }

    [Fact]
    public async Task Login_InactiveAccount_ShouldReturnForbidden403()
    {
        // Arrange
        using var client = CreateTestClient();
        var registerRequest = new RegisterRequest(
            FullName: "Inactive User",
            Email: "inactive_user@example.com",
            Password: "ValidPassword123!",
            PhoneNumber: null
        );
        await client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);

        // Deactivate user in DB
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.FirstAsync(u => u.Email == "inactive_user@example.com");
            user.IsActive = false;
            await db.SaveChangesAsync();
        }

        // Act
        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest("inactive_user@example.com", "ValidPassword123!"));

        // Assert
        loginResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var error = await loginResponse.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);
        error.Should().NotBeNull();
        error!.Code.Should().Be("Auth.AccountDisabled");
    }

    #endregion

    #region 3. Refresh Tests

    [Fact]
    public async Task Refresh_ValidCookie_ShouldRotateTokenAndReturnNewAccessResponse()
    {
        // Arrange
        using var client = CreateTestClient();
        var registerRequest = new RegisterRequest(
            FullName: "Refresh User",
            Email: "refresh_user@example.com",
            Password: "SecurePassword123!",
            PhoneNumber: null
        );
        var regResponse = await client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
        var initialRawRefreshToken = ExtractCookieValue(regResponse, CookieName);
        initialRawRefreshToken.Should().NotBeNullOrWhiteSpace();

        // Act: Refresh using the cookie
        var refreshMsg = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        refreshMsg.Headers.Add("Cookie", $"{CookieName}={initialRawRefreshToken}");
        var refreshResponse = await client.SendAsync(refreshMsg);

        // Assert
        refreshResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await refreshResponse.Content.ReadAsStringAsync();
        json.Should().NotContainEquivalentOf("refreshToken");

        var authResponse = JsonSerializer.Deserialize<AuthResponse>(json, JsonOptions);
        authResponse.Should().NotBeNull();
        authResponse!.AccessToken.Should().NotBeNullOrWhiteSpace();

        // New rotated token must be different from initial
        var rotatedRawRefreshToken = ExtractCookieValue(refreshResponse, CookieName);
        rotatedRawRefreshToken.Should().NotBeNullOrWhiteSpace();
        rotatedRawRefreshToken.Should().NotBe(initialRawRefreshToken);

        // Database should now have the rotated token's hash
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IRefreshTokenHasher>();

        var user = await db.Users.FirstAsync(u => u.Email == "refresh_user@example.com");
        user.RefreshToken.Should().Be(hasher.HashToken(rotatedRawRefreshToken!));
    }

    [Fact]
    public async Task Refresh_OldTokenAfterRotation_ShouldBeRejectedWith401()
    {
        // Arrange
        using var client = CreateTestClient();
        var registerRequest = new RegisterRequest(
            FullName: "Rotation User",
            Email: "rotation_user@example.com",
            Password: "SecurePassword123!",
            PhoneNumber: null
        );
        var regResponse = await client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
        var oldRawToken = ExtractCookieValue(regResponse, CookieName);

        // 1st Refresh: Rotate token
        var refreshMsg1 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        refreshMsg1.Headers.Add("Cookie", $"{CookieName}={oldRawToken}");
        var refreshResponse1 = await client.SendAsync(refreshMsg1);
        refreshResponse1.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act: 2nd Refresh with the OLD token
        var refreshMsg2 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        refreshMsg2.Headers.Add("Cookie", $"{CookieName}={oldRawToken}");
        var refreshResponse2 = await client.SendAsync(refreshMsg2);

        // Assert: Old token is rejected
        refreshResponse2.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var error = await refreshResponse2.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);
        error.Should().NotBeNull();
        error!.Code.Should().Be("Auth.InvalidRefreshToken");
    }

    [Fact]
    public async Task Refresh_ExpiredOrInvalidOrMissingToken_ShouldReturn401()
    {
        using var client = CreateTestClient();

        // Case A: Missing Cookie
        var missingCookieMsg = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        var missingCookieResponse = await client.SendAsync(missingCookieMsg);
        missingCookieResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var errorMissing = await missingCookieResponse.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);
        errorMissing!.Code.Should().Be("Auth.InvalidRefreshToken");

        // Case B: Non-existent / invalid token
        var invalidCookieMsg = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        invalidCookieMsg.Headers.Add("Cookie", $"{CookieName}=non-existent-refresh-token-xyz");
        var invalidCookieResponse = await client.SendAsync(invalidCookieMsg);
        invalidCookieResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var errorInvalid = await invalidCookieResponse.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);
        errorInvalid!.Code.Should().Be("Auth.InvalidRefreshToken");

        // Case C: Expired token
        var registerRequest = new RegisterRequest(
            FullName: "Expired User",
            Email: "expired_token_user@example.com",
            Password: "SecurePassword123!",
            PhoneNumber: null
        );
        var regResponse = await client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
        var rawToken = ExtractCookieValue(regResponse, CookieName);

        // Expire token in DB
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.FirstAsync(u => u.Email == "expired_token_user@example.com");
            user.RefreshTokenExpiryTime = DateTime.UtcNow.AddMinutes(-10);
            await db.SaveChangesAsync();
        }

        var expiredCookieMsg = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        expiredCookieMsg.Headers.Add("Cookie", $"{CookieName}={rawToken}");
        var expiredResponse = await client.SendAsync(expiredCookieMsg);
        expiredResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var errorExpired = await expiredResponse.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);
        errorExpired!.Code.Should().Be("Auth.RefreshTokenExpired");
    }

    #endregion

    #region 4. Logout Tests

    [Fact]
    public async Task Logout_ShouldRevokeToken_DeleteCookie_AndBeIdempotent()
    {
        // Arrange
        using var client = CreateTestClient();
        var registerRequest = new RegisterRequest(
            FullName: "Logout User",
            Email: "logout_test@example.com",
            Password: "SecurePassword123!",
            PhoneNumber: null
        );
        var regResponse = await client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
        var rawToken = ExtractCookieValue(regResponse, CookieName);
        rawToken.Should().NotBeNullOrWhiteSpace();

        // Act 1: Call Logout with active cookie
        var logoutMsg1 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        logoutMsg1.Headers.Add("Cookie", $"{CookieName}={rawToken}");
        var logoutResponse1 = await client.SendAsync(logoutMsg1);

        // Assert 1: Status 204 NoContent
        logoutResponse1.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Assert 1: Cookie deletion header is present
        var deleteCookieHeader = GetRawCookieHeader(logoutResponse1, CookieName);
        deleteCookieHeader.Should().NotBeNull();
        deleteCookieHeader!.ToLowerInvariant().Should().Contain("expires=");

        // Assert 1: Token revoked in Database
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.FirstAsync(u => u.Email == "logout_test@example.com");
            user.RefreshToken.Should().BeNull();
            user.RefreshTokenExpiryTime.Should().BeNull();
        }

        // Act 2: Idempotent call with same revoked cookie
        var logoutMsg2 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        logoutMsg2.Headers.Add("Cookie", $"{CookieName}={rawToken}");
        var logoutResponse2 = await client.SendAsync(logoutMsg2);
        logoutResponse2.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Act 3: Idempotent call with no cookie
        var logoutMsg3 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        var logoutResponse3 = await client.SendAsync(logoutMsg3);
        logoutResponse3.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    #endregion

    #region 5. End-to-End Authorization Test

    [Fact]
    public async Task AccessToken_ShouldAuthorizeProtectedEndpoint_CartGet()
    {
        // Arrange
        using var client = CreateTestClient();
        var registerRequest = new RegisterRequest(
            FullName: "Authorized Cart User",
            Email: "cart_auth_user@example.com",
            Password: "SecurePassword123!",
            PhoneNumber: null
        );
        var regResponse = await client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
        regResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var authResponse = await regResponse.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);
        authResponse.Should().NotBeNull();
        authResponse!.AccessToken.Should().NotBeNullOrWhiteSpace();

        // Act: Call protected endpoint GET /api/v1/carts with Bearer token
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authResponse.AccessToken);
        var cartResponse = await client.GetAsync("/api/v1/carts");

        // Assert: 200 OK
        cartResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var cartDto = await cartResponse.Content.ReadFromJsonAsync<CartDto>(JsonOptions);
        cartDto.Should().NotBeNull();
        cartDto!.Items.Should().BeEmpty();
    }

    #endregion

    #region 6. Database Cleanup Ordering Verification

    [Fact]
    public async Task DatabaseCleanup_Ordering_ProperlyDeletesAllEntitiesWithoutFkViolations()
    {
        // Arrange: Seed user, category, product, cart, and cart item
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var user = new User("seed_user@example.com", "pwd_hash", "Seed User", UserRole.Customer);
            db.Users.Add(user);

            var category = new Category("Category Seed", "category-seed", ComponentType.Accessory);
            db.Categories.Add(category);

            var product = new Product(
                category.Id,
                "Product Seed",
                "product-seed",
                "SKU-SEED",
                "RiuTek",
                100_000m,
                10,
                "https://riutek.test/seed.png",
                ComponentType.Accessory,
                new AccessorySpecification { Details = "Seed" }
            );
            db.Products.Add(product);

            var cart = new Cart(user.Id);
            cart.AddItem(product.Id, 2);
            db.Carts.Add(cart);

            await db.SaveChangesAsync();
        }

        // Act: Execute cleanup
        var cleanupAct = async () => await CleanupDataAsync();

        // Assert: Does not throw FK violation and tables are empty
        await cleanupAct.Should().NotThrowAsync();

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.CartItems.CountAsync()).Should().Be(0);
            (await db.Carts.CountAsync()).Should().Be(0);
            (await db.Products.CountAsync()).Should().Be(0);
            (await db.Categories.CountAsync()).Should().Be(0);
            (await db.Users.CountAsync()).Should().Be(0);
        }
    }

    #endregion
}

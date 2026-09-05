using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace RiuTek.API.IntegrationTest.Infrastructure;

public class RiuTekWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;
    private const string TestJwtCanarySecret = "IntegrationTest_JwtCanary_SecretKey_With_At_Least_32_Bytes_Length!";

    public RiuTekWebApplicationFactory(string connectionString)
    {
        _connectionString = connectionString;
        // Set environment variables so WebApplication.CreateBuilder in Program.Main receives canary values before AddAppDI validates
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", _connectionString);
        Environment.SetEnvironmentVariable("JwtSettings__SecretKey", TestJwtCanarySecret);
        Environment.SetEnvironmentVariable("RedisSettings__Enabled", "false");
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:DefaultConnection", _connectionString);
        builder.UseSetting("JwtSettings:SecretKey", TestJwtCanarySecret);
        builder.UseSetting("RedisSettings:Enabled", "false");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _connectionString,
                ["JwtSettings:SecretKey"] = TestJwtCanarySecret,
                ["RedisSettings:Enabled"] = "false"
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", null);
            Environment.SetEnvironmentVariable("JwtSettings__SecretKey", null);
            Environment.SetEnvironmentVariable("RedisSettings__Enabled", null);
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
        }
        base.Dispose(disposing);
    }
}

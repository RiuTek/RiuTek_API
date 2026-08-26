using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using RiuTek.Application;
using RiuTek.Core;
using RiuTek.Infrastructure;

namespace RiuTek.API;

public static class DependencyInjection
{
    public static IServiceCollection AddAppDI(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddCoreDI()
            .AddApplicationDI()
            .AddInfrastructureDI(configuration);

        // Configure JWT Authentication
        var secretKey = configuration["JwtSettings:SecretKey"] 
            ?? "RiuTek_Super_Secret_Key_For_Jwt_Authentication_2026_Key_Must_Be_Long_And_Secure_123456";
        var issuer = configuration["JwtSettings:Issuer"] ?? "RiuTek.API";
        var audience = configuration["JwtSettings:Audience"] ?? "RiuTek.Client";

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = issuer,
                ValidAudience = audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey))
            };
        });

        services.AddAuthorization();

        return services;
    }
}

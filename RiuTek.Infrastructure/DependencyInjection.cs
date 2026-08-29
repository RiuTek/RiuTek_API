using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Core.Constants;
using RiuTek.Core.Enums;
using RiuTek.Core.Interfaces;
using RiuTek.Infrastructure.Caching;
using RiuTek.Infrastructure.Data;
using RiuTek.Infrastructure.Repositories;
using RiuTek.Infrastructure.Security;
using StackExchange.Redis;

namespace RiuTek.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureDI(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection") 
            ?? "Host=localhost;Port=5432;Database=riutek_db;Username=postgres;Password=postgres";

        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.UseVector();
                npgsqlOptions.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
            });
        });

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<ApplicationDbContext>());
        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));

        // JWT Settings Configuration & Fail-Fast Validation
        var jwtSettings = new JwtSettings();
        configuration.GetSection(JwtSettings.SectionName).Bind(jwtSettings);
        jwtSettings.Validate();
        services.AddSingleton(jwtSettings);

        // Redis & Caching Configuration
        var redisSettings = new RedisSettings();
        configuration.GetSection(RedisSettings.SectionName).Bind(redisSettings);
        redisSettings.Validate();
        services.AddSingleton(redisSettings);

        if (redisSettings.Enabled)
        {
            var redisOptions = ConfigurationOptions.Parse(redisSettings.ConnectionString);
            redisOptions.AbortOnConnectFail = false;
            redisOptions.ConnectTimeout = redisSettings.ConnectTimeoutMs;
            redisOptions.SyncTimeout = redisSettings.SyncTimeoutMs;

            services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisOptions));
            services.AddSingleton<ICacheService, RedisCacheService>();
        }
        else
        {
            services.AddSingleton<ICacheService, NoOpCacheService>();
        }

        // Security & Auth Services
        services.AddHttpContextAccessor();
        services.AddSingleton<IPasswordHasher, Services.PasswordHasher>();
        services.AddScoped<IJwtTokenGenerator, Services.JwtTokenGenerator>();
        services.AddScoped<ICurrentUserService, Services.CurrentUserService>();

        // Configure JWT Authentication
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
                ValidIssuer = jwtSettings.Issuer,
                ValidAudience = jwtSettings.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SecretKey))
            };
        });

        // Configure Authorization Policies
        services.AddAuthorization(options =>
        {
            options.AddPolicy(Policies.ContentManager, policy =>
                policy.RequireRole(UserRole.Admin.ToString(), UserRole.Staff.ToString()));
        });

        return services;
    }
}

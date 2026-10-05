using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RiuTek.Application;
using RiuTek.Core;
using RiuTek.Infrastructure;

namespace RiuTek.API;

public static class DependencyInjection
{
    public static IServiceCollection AddAppDI(
        this IServiceCollection services,
        IConfiguration configuration,
        Microsoft.Extensions.Hosting.IHostEnvironment? environment = null)
    {
        services
            .AddCoreDI()
            .AddApplicationDI()
            .AddInfrastructureDI(configuration, environment);

        return services;
    }
}

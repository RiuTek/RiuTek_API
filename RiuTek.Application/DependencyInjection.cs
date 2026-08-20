using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace RiuTek.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationDI(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
            cfg.AddOpenBehavior(typeof(RiuTek.Application.Common.Behaviors.LoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(RiuTek.Application.Common.Behaviors.ValidationBehavior<,>));
        });
        services.AddValidatorsFromAssembly(assembly);

        return services;
    }
}

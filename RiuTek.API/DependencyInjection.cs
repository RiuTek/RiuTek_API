using RiuTek.Application;
using RiuTek.Infrastructure;

namespace RiuTek.API
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddAppDI(this IServiceCollection services)
        {
            services.AddApplicationDI().AddInfrastructureDI();
                


            return services;
        }
    }
}

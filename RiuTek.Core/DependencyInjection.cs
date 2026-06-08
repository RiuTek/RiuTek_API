using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;

namespace RiuTek.Core
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddACoreDI(this IServiceCollection services)
        {
            return services;
        }
    }
}

using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using RiuTek.API.Controllers;

namespace RiuTek.Application.Test.Controllers;

public class RouteAmbiguityTests
{
    [Fact]
    public void ApiControllers_ShouldNotHaveAmbiguousRouteTemplates()
    {
        var controllerTypes = typeof(ApiControllerBase).Assembly.GetTypes()
            .Where(t => typeof(ApiControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .ToList();

        controllerTypes.Should().NotBeEmpty();

        var routeMap = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var controller in controllerTypes)
        {
            var routeAttr = controller.GetCustomAttribute<RouteAttribute>();
            var baseRoute = routeAttr != null
                ? routeAttr.Template.Replace("[controller]", controller.Name.Replace("Controller", ""), StringComparison.OrdinalIgnoreCase)
                : "";

            var methods = controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
            foreach (var method in methods)
            {
                var httpMethodAttr = method.GetCustomAttributes().OfType<HttpMethodAttribute>().FirstOrDefault();
                if (httpMethodAttr == null) continue;

                var verb = httpMethodAttr.HttpMethods.FirstOrDefault() ?? "GET";
                var actionTemplate = httpMethodAttr.Template ?? "";
                var fullEndpointKey = $"{verb} {baseRoute}/{actionTemplate}".TrimEnd('/');

                routeMap.Add(fullEndpointKey).Should().BeTrue($"Duplicate or ambiguous route detected: '{fullEndpointKey}' on {controller.Name}.{method.Name}");
            }
        }
    }
}

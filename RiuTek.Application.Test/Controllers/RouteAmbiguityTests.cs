using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using RiuTek.API.Controllers;

namespace RiuTek.Application.Test.Controllers;

public class RouteAmbiguityTests
{
    public static string CanonicalizeRouteTemplate(string routeTemplate)
    {
        if (string.IsNullOrWhiteSpace(routeTemplate))
            return string.Empty;

        // Trim leading and trailing slashes
        var normalized = routeTemplate.Trim().Trim('/');

        // Normalize route parameters: e.g. {id:guid} -> {__param__:guid}, {slug} -> {__param__}
        // Match {paramName:constraint} or {paramName}
        normalized = Regex.Replace(normalized, @"\{[a-zA-Z0-9_]+(?<constraint>:[^}]+)?\}", "{__param__${constraint}}");

        return normalized.ToLowerInvariant();
    }

    [Fact]
    public void CanonicalizeRouteTemplate_NormalizesParameterNames_WhilePreservingConstraints()
    {
        var routeA = CanonicalizeRouteTemplate("api/v1/posts/{id:guid}");
        var routeB = CanonicalizeRouteTemplate("api/v1/posts/{otherId:guid}");
        var routeC = CanonicalizeRouteTemplate("api/v1/posts/{slug}");
        var routeD = CanonicalizeRouteTemplate("api/v1/posts/{title}");

        routeA.Should().Be("api/v1/posts/{__param__:guid}");
        routeB.Should().Be("api/v1/posts/{__param__:guid}");
        routeA.Should().Be(routeB, "Differently named route parameters with identical constraints should produce identical canonical routes");

        routeC.Should().Be("api/v1/posts/{__param__}");
        routeD.Should().Be("api/v1/posts/{__param__}");
        routeC.Should().Be(routeD, "Differently named route parameters without constraints should produce identical canonical routes");

        routeA.Should().NotBe(routeC, "Routes with different constraints should produce different canonical routes");
    }

    [Fact]
    public void ApiControllers_ShouldNotHaveAmbiguousOrCollidingCanonicalRoutes()
    {
        var controllerTypes = typeof(ApiControllerBase).Assembly.GetTypes()
            .Where(t => typeof(ApiControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .ToList();

        controllerTypes.Should().NotBeEmpty();

        var canonicalRouteMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var controller in controllerTypes)
        {
            var routeAttr = controller.GetCustomAttribute<RouteAttribute>();
            var baseRoute = routeAttr != null
                ? routeAttr.Template.Replace("[controller]", controller.Name.Replace("Controller", "", StringComparison.OrdinalIgnoreCase), StringComparison.OrdinalIgnoreCase)
                : "";

            var methods = controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
            foreach (var method in methods)
            {
                var httpMethodAttr = method.GetCustomAttributes().OfType<HttpMethodAttribute>().FirstOrDefault();
                if (httpMethodAttr == null) continue;

                var verb = (httpMethodAttr.HttpMethods.FirstOrDefault() ?? "GET").ToUpperInvariant();
                var actionTemplate = httpMethodAttr.Template ?? "";

                var fullRoute = string.IsNullOrWhiteSpace(actionTemplate)
                    ? baseRoute
                    : $"{baseRoute.TrimEnd('/')}/{actionTemplate.TrimStart('/')}";

                var canonicalRoute = CanonicalizeRouteTemplate(fullRoute);
                var endpointKey = $"{verb} {canonicalRoute}";
                var endpointSource = $"{controller.Name}.{method.Name} ({fullRoute})";

                canonicalRouteMap.ContainsKey(endpointKey).Should().BeFalse(
                    $"Collision detected between '{endpointSource}' and '{canonicalRouteMap.GetValueOrDefault(endpointKey)}' on canonical key '{endpointKey}'");

                canonicalRouteMap[endpointKey] = endpointSource;
            }
        }
    }
}

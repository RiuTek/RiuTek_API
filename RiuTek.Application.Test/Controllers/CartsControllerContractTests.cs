using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using RiuTek.API.Controllers;
using RiuTek.Application.DTOs;
using Xunit;

namespace RiuTek.Application.Test.Controllers;

public class CartsControllerContractTests
{
    private readonly MethodInfo _addFromPCBuildMethod;

    public CartsControllerContractTests()
    {
        var method = typeof(CartsController).GetMethod(nameof(CartsController.AddFromPCBuild));
        method.Should().NotBeNull("CartsController must declare AddFromPCBuild action");
        _addFromPCBuildMethod = method!;
    }

    [Fact]
    public void CartsController_HasAuthorizeAttribute_AndActionDoesNotAllowAnonymous()
    {
        typeof(CartsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Should().NotBeEmpty("CartsController must be protected by [Authorize]");

        _addFromPCBuildMethod
            .GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true)
            .Should().BeEmpty("AddFromPCBuild must not have [AllowAnonymous]");
    }

    [Fact]
    public void AddFromPCBuild_HasHttpPostAttribute_WithExpectedRouteTemplate()
    {
        var postAttr = _addFromPCBuildMethod.GetCustomAttribute<HttpPostAttribute>();
        postAttr.Should().NotBeNull("AddFromPCBuild must be an HTTP POST action");
        postAttr!.Template.Should().Be("from-pc-build/{pcBuildId:guid}", "Route template must be exactly 'from-pc-build/{pcBuildId:guid}'");
    }

    [Fact]
    public void AddFromPCBuild_HasOnlyRouteAndCancellationTokenParameters_WithNoBodyParameters()
    {
        var parameters = _addFromPCBuildMethod.GetParameters();

        parameters.Should().HaveCount(2, "AddFromPCBuild should only accept pcBuildId and cancellationToken");

        var pcBuildIdParam = parameters.FirstOrDefault(p => p.Name == "pcBuildId");
        pcBuildIdParam.Should().NotBeNull();
        pcBuildIdParam!.ParameterType.Should().Be(typeof(Guid));
        pcBuildIdParam.GetCustomAttribute<FromRouteAttribute>().Should().NotBeNull("pcBuildId parameter must have [FromRoute]");

        var cancellationTokenParam = parameters.FirstOrDefault(p => p.ParameterType == typeof(CancellationToken));
        cancellationTokenParam.Should().NotBeNull();

        parameters.Should().NotContain(p => p.GetCustomAttribute<FromBodyAttribute>() != null, "No parameter may have [FromBody]");
    }

    [Fact]
    public void AddFromPCBuild_DeclaresExactExpectedProducesResponseTypeAttributes()
    {
        var attrs = _addFromPCBuildMethod.GetCustomAttributes<ProducesResponseTypeAttribute>().ToList();

        attrs.Should().HaveCount(6, "Declared response codes should be 200, 400, 401, 403, 404, 409");

        attrs.Should().Contain(a => a.StatusCode == StatusCodes.Status200OK && a.Type == typeof(CartDto));
        attrs.Should().Contain(a => a.StatusCode == StatusCodes.Status400BadRequest);
        attrs.Should().Contain(a => a.StatusCode == StatusCodes.Status401Unauthorized);
        attrs.Should().Contain(a => a.StatusCode == StatusCodes.Status403Forbidden);
        attrs.Should().Contain(a => a.StatusCode == StatusCodes.Status404NotFound);
        attrs.Should().Contain(a => a.StatusCode == StatusCodes.Status409Conflict);
    }
}

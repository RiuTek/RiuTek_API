using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using RiuTek.API.Controllers;
using RiuTek.Application.Common.Models;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Orders.Queries;
using RiuTek.Core.Common;
using RiuTek.Core.Enums;
using Xunit;

namespace RiuTek.Application.Test.Controllers;

public class OrdersControllerContractTests : IDisposable
{
    private readonly Mock<ISender> _senderMock;
    private readonly ServiceProvider _serviceProvider;
    private readonly OrdersController _controller;

    public OrdersControllerContractTests()
    {
        _senderMock = new Mock<ISender>();
        var services = new ServiceCollection();
        services.AddScoped<ISender>(_ => _senderMock.Object);
        _serviceProvider = services.BuildServiceProvider();

        _controller = new OrdersController
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    RequestServices = _serviceProvider
                }
            }
        };
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
    }

    #region Attribute & Routing Reflection Tests

    [Fact]
    public void OrdersController_HasAuthorizeAttribute()
    {
        typeof(OrdersController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Should().NotBeEmpty("OrdersController must be protected by [Authorize]");
    }

    [Fact]
    public void GetMyOrders_HasCorrectHttpGetAttribute()
    {
        var method = typeof(OrdersController).GetMethod(nameof(OrdersController.GetMyOrders));
        method.Should().NotBeNull();

        var httpGetAttr = method!.GetCustomAttribute<HttpGetAttribute>();
        httpGetAttr.Should().NotBeNull();
        httpGetAttr!.Template.Should().BeNull("Root GET has no additional template segment");
    }

    [Fact]
    public void GetMyOrderById_HasCorrectHttpGetAttributeWithGuidConstraint()
    {
        var method = typeof(OrdersController).GetMethod(nameof(OrdersController.GetMyOrderById));
        method.Should().NotBeNull();

        var httpGetAttr = method!.GetCustomAttribute<HttpGetAttribute>();
        httpGetAttr.Should().NotBeNull();
        httpGetAttr!.Template.Should().Be("{id:guid}", "GET by ID must use {id:guid} route constraint");
    }

    [Fact]
    public void GetMyOrders_HasExpectedProducesResponseTypeAttributes()
    {
        var method = typeof(OrdersController).GetMethod(nameof(OrdersController.GetMyOrders))!;
        var attrs = method.GetCustomAttributes<ProducesResponseTypeAttribute>().ToList();

        attrs.Should().Contain(a => a.StatusCode == StatusCodes.Status200OK && a.Type == typeof(PagedResult<OrderSummaryDto>));
        attrs.Should().Contain(a => a.StatusCode == StatusCodes.Status400BadRequest);
        attrs.Should().Contain(a => a.StatusCode == StatusCodes.Status401Unauthorized);
        attrs.Should().Contain(a => a.StatusCode == StatusCodes.Status403Forbidden);
        attrs.Should().Contain(a => a.StatusCode == StatusCodes.Status404NotFound);
    }

    [Fact]
    public void GetMyOrderById_HasExpectedProducesResponseTypeAttributes()
    {
        var method = typeof(OrdersController).GetMethod(nameof(OrdersController.GetMyOrderById))!;
        var attrs = method.GetCustomAttributes<ProducesResponseTypeAttribute>().ToList();

        attrs.Should().Contain(a => a.StatusCode == StatusCodes.Status200OK && a.Type == typeof(OrderDetailDto));
        attrs.Should().Contain(a => a.StatusCode == StatusCodes.Status400BadRequest);
        attrs.Should().Contain(a => a.StatusCode == StatusCodes.Status401Unauthorized);
        attrs.Should().Contain(a => a.StatusCode == StatusCodes.Status403Forbidden);
        attrs.Should().Contain(a => a.StatusCode == StatusCodes.Status404NotFound);
    }

    #endregion

    #region Action Delegation to Mediator Tests

    [Fact]
    public async Task GetMyOrders_PassesParametersToMediator_AndReturnsOk()
    {
        var pagedResult = PagedResult<OrderSummaryDto>.Create([], 0, 2, 20);
        _senderMock.Setup(s => s.Send(
            It.Is<GetMyOrdersQuery>(q => q.PageIndex == 2 && q.PageSize == 20 && q.Status == OrderStatus.Confirmed),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(pagedResult));

        var actionResult = await _controller.GetMyOrders(pageIndex: 2, pageSize: 20, status: OrderStatus.Confirmed);

        var okResult = actionResult as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.StatusCode.Should().Be(StatusCodes.Status200OK);
        okResult.Value.Should().Be(pagedResult);
    }

    [Fact]
    public async Task GetMyOrderById_PassesIdToMediator_AndReturnsOk()
    {
        var orderId = Guid.NewGuid();
        var detailDto = new OrderDetailDto(
            orderId, "ORD-123", OrderStatus.Confirmed, PaymentMethod.COD, PaymentStatus.Pending,
            "VND", "Cust", "cust@test.com", "0901", "Addr", 100m, 0m, 100m, null, DateTime.UtcNow, []
        );

        _senderMock.Setup(s => s.Send(
            It.Is<GetMyOrderByIdQuery>(q => q.Id == orderId),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(detailDto));

        var actionResult = await _controller.GetMyOrderById(orderId);

        var okResult = actionResult as OkObjectResult;
        okResult.Should().NotBeNull();
        okResult!.StatusCode.Should().Be(StatusCodes.Status200OK);
        okResult.Value.Should().Be(detailDto);
    }

    [Fact]
    public async Task GetMyOrderById_WhenNotFound_ReturnsNotFound()
    {
        var orderId = Guid.NewGuid();
        _senderMock.Setup(s => s.Send(
            It.Is<GetMyOrderByIdQuery>(q => q.Id == orderId),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<OrderDetailDto>(Error.NotFound("Order.NotFound", "Order was not found.")));

        var actionResult = await _controller.GetMyOrderById(orderId);

        var notFoundResult = actionResult as NotFoundObjectResult;
        notFoundResult.Should().NotBeNull();
        notFoundResult!.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    #endregion

    #region JSON Serialization Tests

    [Fact]
    public void OrderSummaryDto_SerializesEnumsAsStrings()
    {
        var summary = new OrderSummaryDto(
            Guid.NewGuid(),
            "ORD-999",
            OrderStatus.Confirmed,
            PaymentMethod.COD,
            PaymentStatus.Pending,
            "VND",
            200_000m,
            0m,
            200_000m,
            3,
            DateTime.UtcNow
        );

        var json = JsonSerializer.Serialize(summary);

        json.Should().Contain("\"Status\":\"Confirmed\"");
        json.Should().Contain("\"PaymentMethod\":\"COD\"");
        json.Should().Contain("\"PaymentStatus\":\"Pending\"");
    }

    [Fact]
    public void OrderDetailDto_SerializesEnumsAsStrings()
    {
        var detail = new OrderDetailDto(
            Guid.NewGuid(),
            "ORD-999",
            OrderStatus.Completed,
            PaymentMethod.COD,
            PaymentStatus.Completed,
            "VND",
            "Customer",
            "c@t.com",
            "0900",
            "Address",
            200_000m,
            0m,
            200_000m,
            "Notes",
            DateTime.UtcNow,
            [new OrderItemDto(Guid.NewGuid(), "Prod", "SKU1", 100m, 2, 200m)]
        );

        var json = JsonSerializer.Serialize(detail);

        json.Should().Contain("\"Status\":\"Completed\"");
        json.Should().Contain("\"PaymentMethod\":\"COD\"");
        json.Should().Contain("\"PaymentStatus\":\"Completed\"");
    }

    [Fact]
    public void CheckoutOrderDto_JsonContract_MaintainsBackwardsCompatibility()
    {
        var checkoutDto = new CheckoutOrderDto(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "ORD-2026-001",
            OrderStatus.Confirmed,
            PaymentMethod.COD,
            PaymentStatus.Pending,
            "VND",
            "Nguyen Van A",
            "a@riutek.test",
            "0901234567",
            "123 Nguyen Hue",
            500_000m,
            50_000m,
            450_000m,
            "Deliver during daytime",
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            [new OrderItemDto(Guid.Parse("22222222-2222-2222-2222-222222222222"), "CPU Intel", "BX8071513700K", 500_000m, 1, 500_000m)]
        );

        var json = JsonSerializer.Serialize(checkoutDto, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        // Assert camelCase keys are intact
        json.Should().Contain("\"id\":\"11111111-1111-1111-1111-111111111111\"");
        json.Should().Contain("\"orderNumber\":\"ORD-2026-001\"");
        json.Should().Contain("\"status\":\"Confirmed\"");
        json.Should().Contain("\"paymentMethod\":\"COD\"");
        json.Should().Contain("\"items\":[");
        json.Should().Contain("\"productName\":\"CPU Intel\"");
        json.Should().Contain("\"totalPrice\":500000");
    }

    #endregion
}

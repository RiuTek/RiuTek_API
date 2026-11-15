using System.Security.Claims;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RiuTek.API.Contracts;
using RiuTek.API.Controllers;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Ai.Queries;
using RiuTek.Core.Common;
using RiuTek.Core.Constants;
using RiuTek.Core.Enums;
using RiuTek.Infrastructure.Caching;
using RiuTek.Infrastructure.Security;
using RiuTek.Infrastructure.Services;
using RiuTek.Infrastructure.Settings;
using StackExchange.Redis;

namespace RiuTek.Application.Test.Features.Ai;

public class AiQuotaAndControllerTests
{
    private static (AiController Controller, Mock<ISender> SenderMock, DefaultHttpContext HttpContext) CreateController(
        AiSettings settings,
        ClaimsPrincipal? user = null)
    {
        var senderMock = new Mock<ISender>();
        var services = new ServiceCollection();
        services.AddSingleton(senderMock.Object);
        services.AddLogging();
        services.AddAuthorization(options =>
        {
            options.AddPolicy(Policies.ContentManager, policy =>
                policy.RequireRole(UserRole.Admin.ToString(), UserRole.Staff.ToString()));
        });

        var sp = services.BuildServiceProvider();
        var authService = sp.GetRequiredService<IAuthorizationService>();
        var jwtSettings = new JwtSettings
        {
            SecretKey = "TEST_SECRET_KEY_AT_LEAST_32_BYTES_LONG_FOR_SIGNING",
            Issuer = "RiuTek",
            Audience = "RiuTek"
        };

        var controller = new AiController(settings, authService, jwtSettings);
        var httpContext = new DefaultHttpContext
        {
            RequestServices = sp
        };

        if (user != null)
        {
            httpContext.User = user;
        }

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        return (controller, senderMock, httpContext);
    }

    private static ClaimsPrincipal CreatePrincipal(Guid userId, UserRole role)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, role.ToString())
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    [Fact]
    public async Task Ask_WhenAiDisabled_Returns503ProblemDetails()
    {
        var settings = new AiSettings { Enabled = false };
        var (controller, senderMock, _) = CreateController(settings);

        var actionResult = await controller.Ask(new AskAiRequest("Xin chào"), CancellationToken.None);

        var obj = actionResult.Should().BeOfType<ObjectResult>().Subject;
        obj.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        var problem = obj.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Title.Should().Be("Ai.Disabled");
        senderMock.Verify(s => s.Send(It.IsAny<AskAiConsultantQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Ask_WhenInternalTestModeAndUnauthenticated_Returns401ProblemDetails()
    {
        var settings = new AiSettings
        {
            Enabled = true,
            AllowAnonymous = false,
            Groq = new GroqSettings { ApiKey = "gsk_test_fake_key_not_real" }
        };
        var (controller, senderMock, _) = CreateController(settings, user: null);

        var actionResult = await controller.Ask(new AskAiRequest("Xin chào"), CancellationToken.None);

        var obj = actionResult.Should().BeOfType<ObjectResult>().Subject;
        obj.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        senderMock.Verify(s => s.Send(It.IsAny<AskAiConsultantQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Ask_WhenGuestAccessIsDisabled_SendsAuthenticatedCustomerIdToHandler()
    {
        var settings = new AiSettings
        {
            Enabled = true,
            AllowAnonymous = false,
            Groq = new GroqSettings { ApiKey = "gsk_test_fake_key_not_real" }
        };
        var customerId = Guid.NewGuid();
        var customerUser = CreatePrincipal(customerId, UserRole.Customer);
        var (controller, senderMock, _) = CreateController(settings, user: customerUser);
        senderMock.Setup(s => s.Send(It.IsAny<AskAiConsultantQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new AiConsultantResponseDto("answered", "OK", [], [])));

        var actionResult = await controller.Ask(new AskAiRequest("Xin chào"), CancellationToken.None);

        actionResult.Should().BeOfType<OkObjectResult>();
        senderMock.Verify(s => s.Send(It.Is<AskAiConsultantQuery>(query =>
            query.CustomerId == customerId && query.ClientQuotaKey == $"user:{customerId:D}"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Ask_WhenInternalTestModeAndStaffRole_AllowsRequest()
    {
        var settings = new AiSettings
        {
            Enabled = true,
            AllowAnonymous = false,
            Groq = new GroqSettings { ApiKey = "gsk_test_fake_key_not_real" }
        };
        var staffId = Guid.NewGuid();
        var staffUser = CreatePrincipal(staffId, UserRole.Staff);
        var (controller, senderMock, _) = CreateController(settings, user: staffUser);

        senderMock
            .Setup(s => s.Send(It.IsAny<AskAiConsultantQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new AiConsultantResponseDto("answered", "OK", [], [])));

        var actionResult = await controller.Ask(new AskAiRequest("Câu hỏi kỹ thuật"), CancellationToken.None);

        var ok = actionResult.Should().BeOfType<OkObjectResult>().Subject;
        ok.StatusCode.Should().Be(StatusCodes.Status200OK);
        senderMock.Verify(s => s.Send(
            It.Is<AskAiConsultantQuery>(q => q.ClientQuotaKey == $"user:{staffId}"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Ask_WhenAllowAnonymousTrueAndRateLimited_SetsRetryAfterHeaderAndReturns429()
    {
        var settings = new AiSettings
        {
            Enabled = true,
            AllowAnonymous = true,
            Groq = new GroqSettings { ApiKey = "gsk_test_fake_key_not_real" }
        };
        var (controller, senderMock, httpContext) = CreateController(settings, user: null);

        senderMock
            .Setup(s => s.Send(It.IsAny<AskAiConsultantQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<AiConsultantResponseDto>(
                Error.TooManyRequests("Ai.TurnLimitExceeded", "Quá giới hạn lượt hỏi.", 420)));

        var actionResult = await controller.Ask(new AskAiRequest("Câu hỏi"), CancellationToken.None);

        var obj = actionResult.Should().BeOfType<ObjectResult>().Subject;
        obj.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
        httpContext.Response.Headers.RetryAfter.ToString().Should().Be("420");
        httpContext.Response.Headers.SetCookie.ToString().Should().Contain(AiController.GuestSessionCookieName);
        httpContext.Response.Headers.SetCookie.ToString().ToLowerInvariant().Should().Contain("httponly");
        httpContext.Response.Headers.SetCookie.ToString().ToLowerInvariant().Should().Contain("secure");
    }

    [Fact]
    public async Task RedisAiQuotaService_WhenRedisDisabledOrDisconnected_ReturnsRedisUnavailable()
    {
        var redisSettings = new RedisSettings { Enabled = false };
        var aiSettings = new AiSettings { Enabled = true };
        var service = new RedisAiQuotaService(redisSettings, aiSettings, NullLogger<RedisAiQuotaService>.Instance, multiplexer: null);

        var turnRes = await service.ReserveTurnAsync("user:1");
        turnRes.IsAllowed.Should().BeFalse();
        turnRes.IsRedisUnavailable.Should().BeTrue();
    }

    [Fact]
    public async Task RedisAiQuotaService_ConcurrentReservations_EnforcesExactLimitAtomically()
    {
        var counter = 0;
        var lockObj = new object();

        var dbMock = new Mock<IDatabase>();
        dbMock
            .Setup(d => d.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]>(),
                It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()))
            .Returns<string, RedisKey[], RedisValue[], CommandFlags>((_, _, values, _) =>
            {
                var limit = (int)values[0];
                var window = (int)values[1];
                lock (lockObj)
                {
                    if (counter >= limit)
                    {
                        return Task.FromResult(RedisResult.Create(new RedisResult[]
                        {
                            RedisResult.Create(0),
                            RedisResult.Create(window)
                        }));
                    }

                    counter++;
                    return Task.FromResult(RedisResult.Create(new RedisResult[]
                    {
                        RedisResult.Create(1),
                        RedisResult.Create(window)
                    }));
                }
            });

        var muxMock = new Mock<IConnectionMultiplexer>();
        muxMock.Setup(m => m.IsConnected).Returns(true);
        muxMock.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(dbMock.Object);

        var redisSettings = new RedisSettings { Enabled = true, InstanceName = "test:" };
        var aiSettings = new AiSettings
        {
            Enabled = true,
            Limits = new AiLimitsSettings { TurnsPer15Minutes = 5 }
        };

        var quotaService = new RedisAiQuotaService(
            redisSettings,
            aiSettings,
            NullLogger<RedisAiQuotaService>.Instance,
            muxMock.Object);

        var tasks = Enumerable.Range(0, 20)
            .Select(_ => quotaService.ReserveTurnAsync("user:concurrent"))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        results.Count(r => r.IsAllowed).Should().Be(5);
        results.Count(r => !r.IsAllowed && !r.IsRedisUnavailable).Should().Be(15);
    }
}

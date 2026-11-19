using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RiuTek.API.Contracts;
using RiuTek.Application.Common.Interfaces;
using RiuTek.Application.Features.Ai.Queries;
using RiuTek.Core.Common;
using RiuTek.Core.Constants;
using RiuTek.Core.Enums;
using RiuTek.Infrastructure.Security;

namespace RiuTek.API.Controllers;

public class AiController : ApiControllerBase
{
    public const string GuestSessionCookieName = "riutek.ai_session";
    private static readonly byte[] FallbackSigningKey = RandomNumberGenerator.GetBytes(32);

    private readonly IAiRuntimeSettings _aiSettings;
    private readonly IAuthorizationService _authorizationService;
    private readonly byte[] _cookieSigningKey;

    public AiController(
        IAiRuntimeSettings aiSettings,
        IAuthorizationService authorizationService,
        JwtSettings? jwtSettings = null)
    {
        _aiSettings = aiSettings;
        _authorizationService = authorizationService;
        _cookieSigningKey = !string.IsNullOrWhiteSpace(jwtSettings?.SecretKey)
            ? Encoding.UTF8.GetBytes(jwtSettings.SecretKey)
            : FallbackSigningKey;
    }

    [HttpPost("ask")]
    [AllowAnonymous]
    [RequestSizeLimit(16 * 1024)]
    public async Task<IActionResult> Ask(
        [FromBody] AskAiRequest? request,
        CancellationToken cancellationToken)
    {
        if (!_aiSettings.Enabled)
        {
            return CreateProblemResponse(
                StatusCodes.Status503ServiceUnavailable,
                "Ai.Disabled",
                "Tính năng tư vấn AI hiện đang tắt.");
        }

        Guid? customerId = null;
        if (User?.Identity?.IsAuthenticated == true && User.IsInRole(nameof(UserRole.Customer)))
        {
            var subject = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            if (!Guid.TryParse(subject, out var parsedId) || parsedId == Guid.Empty)
            {
                return CreateProblemResponse(StatusCodes.Status401Unauthorized, "Ai.Unauthorized", "Phiên đăng nhập không hợp lệ.");
            }
            customerId = parsedId;
        }
        else if (!_aiSettings.AllowAnonymous)
        {
            if (User?.Identity?.IsAuthenticated != true)
            {
                return CreateProblemResponse(
                    StatusCodes.Status401Unauthorized,
                    "Ai.Unauthorized",
                    "Bạn cần đăng nhập để dùng tư vấn AI.");
            }

            var authResult = await _authorizationService.AuthorizeAsync(User, null, Policies.ContentManager);
            if (!authResult.Succeeded)
            {
                return CreateProblemResponse(
                    StatusCodes.Status403Forbidden,
                    "Ai.Forbidden",
                    "Tài khoản không có quyền dùng tư vấn AI.");
            }
        }

        if (request == null)
        {
            return CreateProblemResponse(
                StatusCodes.Status400BadRequest,
                "Validation.Failed",
                "Payload yêu cầu không hợp lệ.");
        }

        var clientQuotaKey = customerId.HasValue ? $"user:{customerId.Value:D}" : ResolveOrIssueClientQuotaKey();

        var query = new AskAiConsultantQuery(
            Question: request.Question ?? string.Empty,
            ProductSlug: request.ProductSlug,
            History: request.History,
            ClientQuotaKey: clientQuotaKey,
            CustomerId: customerId,
            PreviousBuild: request.PreviousBuild);

        var result = await Mediator.Send(query, cancellationToken);
        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        return MapAiErrorToProblemResponse(result.Error);
    }

    private string ResolveOrIssueClientQuotaKey()
    {
        if (User?.Identity?.IsAuthenticated == true)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            if (!string.IsNullOrWhiteSpace(userId))
            {
                return $"user:{userId.Trim()}";
            }
        }

        if (HttpContext?.Request?.Cookies != null &&
            HttpContext.Request.Cookies.TryGetValue(GuestSessionCookieName, out var rawCookie) &&
            TryValidateSignedSessionId(rawCookie, out var validSessionId))
        {
            return $"guest:{validSessionId}";
        }

        var newSessionId = Guid.NewGuid().ToString("N");
        var signedValue = SignSessionId(newSessionId);

        HttpContext?.Response?.Cookies?.Append(
            GuestSessionCookieName,
            signedValue,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                Path = "/api/v1/ai",
                MaxAge = TimeSpan.FromDays(1)
            });

        return $"guest:{newSessionId}";
    }

    private string SignSessionId(string sessionId)
    {
        using var hmac = new HMACSHA256(_cookieSigningKey);
        var sigBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(sessionId));
        var sigHex = Convert.ToHexString(sigBytes).ToLowerInvariant();
        return $"{sessionId}.{sigHex}";
    }

    private bool TryValidateSignedSessionId(string? rawCookie, out string sessionId)
    {
        sessionId = string.Empty;
        if (string.IsNullOrWhiteSpace(rawCookie))
        {
            return false;
        }

        var parts = rawCookie.Split('.', 2);
        if (parts.Length != 2 || parts[0].Length != 32 || !Guid.TryParseExact(parts[0], "N", out _))
        {
            return false;
        }

        using var hmac = new HMACSHA256(_cookieSigningKey);
        var expectedBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(parts[0]));
        byte[] actualBytes;
        try
        {
            actualBytes = Convert.FromHexString(parts[1]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (!CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes))
        {
            return false;
        }

        sessionId = parts[0];
        return true;
    }

    private IActionResult MapAiErrorToProblemResponse(Error error)
    {
        if (error.RetryAfterSeconds is > 0 && HttpContext?.Response != null)
        {
            HttpContext.Response.Headers.RetryAfter = error.RetryAfterSeconds.Value.ToString(CultureInfo.InvariantCulture);
        }

        var statusCode = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.TooManyRequests => StatusCodes.Status429TooManyRequests,
            ErrorType.Unavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status400BadRequest
        };

        return CreateProblemResponse(statusCode, error.Code, error.Description);
    }

    private ObjectResult CreateProblemResponse(int statusCode, string title, string detail)
    {
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = HttpContext?.Request?.Path.Value
        };

        return new ObjectResult(problem)
        {
            StatusCode = statusCode,
            ContentTypes = { "application/problem+json" }
        };
    }
}

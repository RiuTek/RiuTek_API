using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RiuTek.API.Contracts;
using RiuTek.Application.DTOs;
using RiuTek.Application.Features.Auth.Commands;
using RiuTek.Core.Common;
using RiuTek.Infrastructure.Security;

namespace RiuTek.API.Controllers;

public class AuthController : ApiControllerBase
{
    private readonly RefreshCookieSettings _cookieSettings;

    public AuthController(RefreshCookieSettings cookieSettings)
    {
        _cookieSettings = cookieSettings;
    }

    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken = default)
    {
        var command = new RegisterCommand(
            request.FullName,
            request.Email,
            request.Password,
            request.PhoneNumber);

        var result = await Mediator.Send(command, cancellationToken);
        if (!result.IsSuccess)
        {
            return HandleResult(result);
        }

        SetRefreshTokenCookie(result.Value.RefreshToken);

        var response = new AuthResponse(
            result.Value.AccessToken,
            result.Value.ExpiresInSeconds,
            result.Value.User);

        return StatusCode(StatusCodes.Status201Created, response);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken = default)
    {
        var command = new LoginCommand(request.Email, request.Password);
        var result = await Mediator.Send(command, cancellationToken);
        if (!result.IsSuccess)
        {
            return HandleResult(result);
        }

        SetRefreshTokenCookie(result.Value.RefreshToken);

        var response = new AuthResponse(
            result.Value.AccessToken,
            result.Value.ExpiresInSeconds,
            result.Value.User);

        return Ok(response);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken = default)
    {
        if (!Request.Cookies.TryGetValue(_cookieSettings.CookieName, out var refreshToken) ||
            string.IsNullOrWhiteSpace(refreshToken))
        {
            return HandleResult(Result.Failure<AuthResponseDto>(Error.Unauthorized(
                "Auth.InvalidRefreshToken",
                "Refresh token không hợp lệ hoặc không tìm thấy trong cookie.")));
        }

        var command = new RefreshTokenCommand(refreshToken);
        var result = await Mediator.Send(command, cancellationToken);
        if (!result.IsSuccess)
        {
            return HandleResult(result);
        }

        SetRefreshTokenCookie(result.Value.RefreshToken);

        var response = new AuthResponse(
            result.Value.AccessToken,
            result.Value.ExpiresInSeconds,
            result.Value.User);

        return Ok(response);
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken = default)
    {
        Request.Cookies.TryGetValue(_cookieSettings.CookieName, out var refreshToken);

        DeleteRefreshTokenCookie();

        await Mediator.Send(new LogoutCommand(refreshToken), cancellationToken);

        return NoContent();
    }

    private void SetRefreshTokenCookie(string refreshToken)
    {
        var cookieOptions = new CookieOptions
        {
            HttpOnly = _cookieSettings.HttpOnly,
            Secure = _cookieSettings.Secure,
            SameSite = _cookieSettings.SameSite,
            Path = _cookieSettings.Path,
            Expires = DateTimeOffset.UtcNow.AddDays(_cookieSettings.ExpiryDays)
        };

        Response.Cookies.Append(_cookieSettings.CookieName, refreshToken, cookieOptions);
    }

    private void DeleteRefreshTokenCookie()
    {
        var cookieOptions = new CookieOptions
        {
            HttpOnly = _cookieSettings.HttpOnly,
            Secure = _cookieSettings.Secure,
            SameSite = _cookieSettings.SameSite,
            Path = _cookieSettings.Path,
            Expires = DateTimeOffset.UtcNow.AddDays(-1)
        };

        Response.Cookies.Delete(_cookieSettings.CookieName, cookieOptions);
    }
}

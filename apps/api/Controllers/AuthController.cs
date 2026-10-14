using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Yanyun.Api.Common;
using Yanyun.Api.Infrastructure.Authentication;
using Yanyun.Api.Modules.Auth;

namespace Yanyun.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(IAntiforgery antiforgery, AuthService auth, AuthRedisStore store,
    IWebHostEnvironment environment) : ControllerBase
{
    private const string RefreshCookie = "yanyun.refresh";
    private string ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    [AllowAnonymous, HttpPost("email-registration/code"), ServiceFilter(typeof(CsrfGuard))]
    public async Task<ActionResult<ApiResponse<CodeChallenge>>> SendRegistrationCode(
        EmailCodeRequest request, CancellationToken cancellationToken)
        => Ok(ApiResponse.Success(await auth.SendCodeAsync(request.Email, ClientIp, cancellationToken)));

    [AllowAnonymous, HttpPost("register"), ServiceFilter(typeof(CsrfGuard))]
    [ProducesResponseType<ApiResponse<RegistrationResult>>(201)]
    public async Task<ActionResult<ApiResponse<RegistrationResult>>> Register(
        RegisterRequest request, CancellationToken cancellationToken)
    {
        var result = await auth.RegisterAsync(request, ClientIp, cancellationToken);
        SetSession(result.Session);
        return StatusCode(201, ApiResponse.Success(new RegistrationResult(result.Session.Auth.AccessToken,
            "Bearer", result.Session.Auth.ExpiresIn, result.UserId)));
    }

    [AllowAnonymous, HttpPost("login"), ServiceFilter(typeof(CsrfGuard))]
    public async Task<ActionResult<ApiResponse<AuthResult>>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var session = await auth.LoginAsync(request, ClientIp, cancellationToken);
        SetSession(session);
        return Ok(ApiResponse.Success(session.Auth));
    }

    [AllowAnonymous, HttpPost("refresh"), ServiceFilter(typeof(CsrfGuard))]
    public async Task<ActionResult<ApiResponse<AuthResult>>> Refresh(CancellationToken cancellationToken)
    {
        try
        {
            var session = await auth.RefreshAsync(Request.Cookies[RefreshCookie], cancellationToken);
            SetSession(session);
            return Ok(ApiResponse.Success(session.Auth));
        }
        catch (BusinessException exception) when (exception.Status is 401 or 403)
        {
            Response.Cookies.Delete(RefreshCookie, CookieOptions());
            throw;
        }
    }

    [AllowAnonymous, HttpPost("logout"), ServiceFilter(typeof(CsrfGuard))]
    public async Task<ActionResult<ApiResponse<LogoutResponse>>> Logout()
    {
        await store.RevokeSessionAsync(Request.Cookies[RefreshCookie]);
        Response.Cookies.Delete(RefreshCookie, CookieOptions());
        return Ok(ApiResponse.Success(new LogoutResponse(true)));
    }

    private CookieOptions CookieOptions() => new()
    {
        Path = "/api/v1/auth", HttpOnly = true, SameSite = SameSiteMode.Lax,
        Secure = !environment.IsDevelopment() || Request.IsHttps
    };

    private void SetSession(SessionResult session)
    {
        var options = CookieOptions();
        options.Expires = session.ExpiresAt;
        Response.Cookies.Append(RefreshCookie, session.RefreshToken, options);
    }

    [AllowAnonymous]
    [HttpGet("csrf")]
    [ProducesResponseType<ApiResponse<CsrfResponse>>(StatusCodes.Status200OK)]
    public ActionResult<ApiResponse<CsrfResponse>> GetCsrf()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(ApiResponse.Success(new CsrfResponse(tokens.RequestToken!)));
    }
}

public sealed record CsrfResponse(string RequestToken);
public sealed record LogoutResponse(bool Success);

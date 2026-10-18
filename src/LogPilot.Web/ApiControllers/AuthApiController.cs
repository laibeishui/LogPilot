using LogPilot.Application.Identity;
using LogPilot.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LogPilot.Web.ApiControllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthApiController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IUserManagementService _users;
    private readonly AuthCookieService _cookies;

    public AuthApiController(
        IAuthService authService,
        IUserManagementService users,
        AuthCookieService cookies)
    {
        _authService = authService;
        _users = users;
        _cookies = cookies;
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await _authService.ValidateLoginAsync(request, cancellationToken);
        await _cookies.SignInAsync(HttpContext, user);
        return Ok(user);
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await _cookies.SignOutAsync(HttpContext);
        return Ok(new { ok = true });
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var user = await _authService.GetCurrentUserAsync(userId, cancellationToken);
        return user is null ? Unauthorized() : Ok(user);
    }

    /// <summary>当前用户修改自己的密码。</summary>
    [Authorize]
    [HttpPut("password")]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        await _users.ChangeOwnPasswordAsync(userId, request, cancellationToken);
        return Ok(new { ok = true });
    }
}

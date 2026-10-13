using LogPilot.Application.Identity;
using LogPilot.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LogPilot.Web.ApiControllers;

[ApiController]
[AllowAnonymous]
[Route("api/v1/setup")]
public sealed class SetupApiController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly AuthCookieService _cookies;

    public SetupApiController(IAuthService authService, AuthCookieService cookies)
    {
        _authService = authService;
        _cookies = cookies;
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken cancellationToken)
    {
        var needsSetup = await _authService.NeedsSetupAsync(cancellationToken);
        return Ok(new { needsSetup });
    }

    [HttpPost("admin")]
    public async Task<IActionResult> CreateAdmin(
        [FromBody] SetupAdminRequest request,
        CancellationToken cancellationToken)
    {
        var user = await _authService.SetupAdminAsync(request, cancellationToken);
        await _cookies.SignInAsync(HttpContext, user);
        return Ok(user);
    }
}

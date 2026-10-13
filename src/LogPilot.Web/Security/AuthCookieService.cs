using System.Security.Claims;
using LogPilot.Application.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace LogPilot.Web.Security;

public sealed class AuthCookieService
{
    public const string Scheme = CookieAuthenticationDefaults.AuthenticationScheme;

    public async Task SignInAsync(HttpContext httpContext, CurrentUserDto user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(LogPilotClaimTypes.UserId, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(LogPilotClaimTypes.IsSystemAdmin, user.IsSystemAdmin ? "1" : "0")
        };

        var identity = new ClaimsIdentity(claims, Scheme);
        var principal = new ClaimsPrincipal(identity);

        await httpContext.SignInAsync(
            Scheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7),
                AllowRefresh = true
            });
    }

    public Task SignOutAsync(HttpContext httpContext)
        => httpContext.SignOutAsync(Scheme);
}

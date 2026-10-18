using System.Security.Claims;
using LogPilot.Core.Abstractions;
using LogPilot.Core.Enums;
using LogPilot.Web.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace LogPilot.Web.Middleware;

/// <summary>
/// 每个已认证请求回查一次 lp_user，使「账号停用」与「系统管理员升降级」即时生效，
/// 而不必等待 Cookie（7 天）过期。
/// </summary>
public sealed class UserStateMiddleware
{
    private readonly RequestDelegate _next;

    public UserStateMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IUserRepository users)
    {
        if (context.User.Identity?.IsAuthenticated == true && context.User.TryGetUserId(out var userId))
        {
            var account = await users.FindByIdAsync(userId, context.RequestAborted);

            if (account is null || account.Status != EntityStatus.Enabled)
            {
                await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                context.User = new ClaimsPrincipal(new ClaimsIdentity());
            }
            else if (context.User.Identity is ClaimsIdentity identity)
            {
                var expected = account.IsSystemAdmin ? "1" : "0";
                var current = identity.FindFirst(LogPilotClaimTypes.IsSystemAdmin);
                if (!string.Equals(current?.Value, expected, StringComparison.Ordinal))
                {
                    if (current is not null)
                    {
                        identity.RemoveClaim(current);
                    }

                    identity.AddClaim(new Claim(LogPilotClaimTypes.IsSystemAdmin, expected));
                }
            }
        }

        await _next(context);
    }
}

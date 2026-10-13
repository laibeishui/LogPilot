using LogPilot.Application.Identity;

namespace LogPilot.Web.Middleware;

/// <summary>
/// 无管理员时强制走初始化流程；已初始化后拒绝重复访问 /setup。
/// </summary>
public sealed class SetupGateMiddleware
{
    private static readonly PathString[] AllowedWhenNeedsSetup =
    [
        new("/setup"),
        new("/api/v1/setup"),
        new("/api/v1/antiforgery"),
        new("/api/v1/ingest"),
        new("/v1/logs"),
        new("/health/live"),
        new("/health/ready")
    ];

    private readonly RequestDelegate _next;

    public SetupGateMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IAuthService authService)
    {
        var path = context.Request.Path;
        var needsSetup = await authService.NeedsSetupAsync(context.RequestAborted);

        if (needsSetup)
        {
            if (!IsAllowedWhenNeedsSetup(path))
            {
                if (path.StartsWithSegments("/api"))
                {
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        code = "SETUP_REQUIRED",
                        message = "系统尚未初始化，请先创建管理员。",
                        requestId = context.TraceIdentifier
                    });
                    return;
                }

                context.Response.Redirect("/setup");
                return;
            }
        }
        else if (path.StartsWithSegments("/setup"))
        {
            context.Response.Redirect("/account/login");
            return;
        }

        await _next(context);
    }

    private static bool IsAllowedWhenNeedsSetup(PathString path)
    {
        if (path.StartsWithSegments("/css")
            || path.StartsWithSegments("/js")
            || path.StartsWithSegments("/lib")
            || path.StartsWithSegments("/favicon"))
        {
            return true;
        }

        return AllowedWhenNeedsSetup.Any(allowed => path.Equals(allowed, StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments(allowed));
    }
}

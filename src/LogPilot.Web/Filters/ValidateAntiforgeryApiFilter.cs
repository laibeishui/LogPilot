using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace LogPilot.Web.Filters;

/// <summary>
/// 对 Cookie 会话下的变更型管理 API 校验 CSRF。
/// </summary>
public sealed class ValidateAntiforgeryApiFilter : IAsyncActionFilter
{
    private readonly IAntiforgery _antiforgery;

    public ValidateAntiforgeryApiFilter(IAntiforgery antiforgery)
    {
        _antiforgery = antiforgery;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var method = context.HttpContext.Request.Method;
        if (HttpMethods.IsPost(method)
            || HttpMethods.IsPut(method)
            || HttpMethods.IsPatch(method)
            || HttpMethods.IsDelete(method))
        {
            var path = context.HttpContext.Request.Path;
            // 管理 API 需要 CSRF；日志接入使用 Bearer Token，不走 Cookie CSRF
            if (path.StartsWithSegments("/api/v1")
                && !path.StartsWithSegments("/api/v1/ingest"))
            {
                try
                {
                    await _antiforgery.ValidateRequestAsync(context.HttpContext);
                }
                catch (AntiforgeryValidationException)
                {
                    context.Result = new ObjectResult(new
                    {
                        code = "ANTIFORGERY_FAILED",
                        message = "请求缺少有效的防伪凭据。",
                        requestId = context.HttpContext.TraceIdentifier
                    })
                    {
                        StatusCode = StatusCodes.Status400BadRequest
                    };
                    return;
                }
            }
        }

        await next();
    }
}

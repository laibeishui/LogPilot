using System.Text.Json;
using LogPilot.Application.Common;

namespace LogPilot.Web.Middleware;

/// <summary>
/// 为 /api 请求兜底统一 JSON 错误体：任何未被 MVC 过滤器处理的异常都返回
/// { code, message, requestId }，避免 API 客户端收到 HTML 错误页或空响应体。
/// </summary>
public sealed class ApiExceptionMiddleware
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly RequestDelegate _next;
    private readonly ILogger<ApiExceptionMiddleware> _logger;

    public ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        try
        {
            await _next(context).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // 客户端主动断开，无需写出错误体
        }
        catch (AppException ex)
        {
            // 业务异常通常已由 ApiExceptionFilter 处理；此处兜底过滤不到的路径（如中间件内抛出）。
            await WriteAsync(context, ex.StatusCode, ex.Code, ex.Message).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理 API 请求 {Path} 时发生未处理异常。", context.Request.Path);
            await WriteAsync(
                context,
                StatusCodes.Status500InternalServerError,
                "INTERNAL_ERROR",
                "服务器内部错误。").ConfigureAwait(false);
        }
    }

    private static async Task WriteAsync(HttpContext context, int statusCode, string code, string message)
    {
        if (context.Response.HasStarted)
        {
            // 响应已开始（如 SSE 长连接），无法再改写状态码与错误体
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";

        var payload = new ApiError
        {
            Code = code,
            Message = message,
            RequestId = context.TraceIdentifier
        };

        await context.Response
            .WriteAsync(JsonSerializer.Serialize(payload, Json), context.RequestAborted)
            .ConfigureAwait(false);
    }
}

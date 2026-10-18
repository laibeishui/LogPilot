using System.Text.Json;
using LogPilot.Application.Common;
using LogPilot.Application.Logs;
using LogPilot.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LogPilot.Web.ApiControllers;

[ApiController]
[Authorize]
[Route("api/v1/logs")]
public sealed class LogsApiController : ControllerBase
{
    private static readonly JsonSerializerOptions StreamJson = new(JsonSerializerDefaults.Web);

    private readonly ILogQueryService _logs;

    public LogsApiController(ILogQueryService logs)
    {
        _logs = logs;
    }

    [HttpPost("search")]
    public async Task<IActionResult> Search(
        [FromBody] LogSearchRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var result = await _logs.SearchAsync(userId, User.IsSystemAdmin(), request, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetDetail(
        string id,
        [FromQuery] long projectId,
        [FromQuery] DateTimeOffset? startTime,
        [FromQuery] DateTimeOffset? endTime,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var detail = await _logs.GetDetailAsync(
            userId,
            User.IsSystemAdmin(),
            projectId,
            id,
            startTime,
            endTime,
            cancellationToken);
        return Ok(detail);
    }

    [HttpGet("context")]
    public async Task<IActionResult> Context(
        [FromQuery] long projectId,
        [FromQuery] DateTimeOffset? timestamp,
        [FromQuery] int? beforeMinutes,
        [FromQuery] int? afterMinutes,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var context = await _logs.GetContextAsync(
            userId,
            User.IsSystemAdmin(),
            projectId,
            timestamp,
            beforeMinutes,
            afterMinutes,
            limit,
            cancellationToken);
        return Ok(context);
    }

    [HttpGet("filters")]
    public async Task<IActionResult> Filters(
        [FromQuery] long projectId,
        [FromQuery] DateTimeOffset? startTime,
        [FromQuery] DateTimeOffset? endTime,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var filters = await _logs.GetFiltersAsync(
            userId,
            User.IsSystemAdmin(),
            projectId,
            startTime,
            endTime,
            cancellationToken);
        return Ok(filters);
    }

    /// <summary>
    /// 实时跟随日志（Server-Sent Events）。前端用 EventSource 订阅，逐条接收新日志。
    /// </summary>
    [HttpGet("tail")]
    public async Task Tail(
        [FromQuery] long projectId,
        [FromQuery] string? service,
        [FromQuery] string? level,
        [FromQuery] string? keyword,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";
        Response.Headers.Connection = "keep-alive";

        var gate = new SemaphoreSlim(1, 1);

        async Task WriteRawAsync(string text, CancellationToken ct)
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await Response.WriteAsync(text, ct).ConfigureAwait(false);
                await Response.Body.FlushAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }

        // 心跳：防止代理/网关因空闲回收长连接
        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeat = Task.Run(async () =>
        {
            try
            {
                while (!heartbeatCts.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(15), heartbeatCts.Token).ConfigureAwait(false);
                    await WriteRawAsync(": ping\n\n", heartbeatCts.Token).ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
                // 连接已断开或已取消
            }
        }, CancellationToken.None);

        try
        {
            // 注意：先不写任何内容，让建连阶段的权限校验失败能够返回非 200 状态码，
            // 从而避免 EventSource 反复重连。首条日志到达（或心跳）时再确认连接。
            var connected = false;

            await foreach (var item in _logs.TailAsync(
                userId,
                User.IsSystemAdmin(),
                projectId,
                service,
                level,
                keyword,
                cancellationToken).ConfigureAwait(false))
            {
                if (!connected)
                {
                    await WriteRawAsync(": connected\n\n", cancellationToken).ConfigureAwait(false);
                    connected = true;
                }

                var payload = JsonSerializer.Serialize(item, StreamJson);
                await WriteRawAsync($"event: log\ndata: {payload}\n\n", cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // 客户端正常断开
        }
        catch (AppException) when (!Response.HasStarted)
        {
            // 建连即被拒（如无权访问该项目）：尚未写出任何内容，交给 ApiExceptionFilter
            // 返回统一的非 200 响应，EventSource 只会失败一次而不会陷入重连循环。
            // 这里必须先清掉已预设的 SSE Content-Type，否则 JSON 错误体会因内容协商失败变成 406。
            Response.ContentType = null;
            Response.Headers.Remove("X-Accel-Buffering");
            throw;
        }
        catch (AppException ex)
        {
            // 长连接中途撤销权限：先明确告知前端原因（前端据此停止自动重连），再结束流。
            var payload = JsonSerializer.Serialize(
                new { code = ex.Code, message = ex.Message },
                StreamJson);
            try
            {
                await WriteRawAsync(
                    $"event: denied\ndata: {payload}\n\n",
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // 忽略：连接已不可写
            }
        }
        finally
        {
            await heartbeatCts.CancelAsync().ConfigureAwait(false);
            try
            {
                await heartbeat.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // 忽略后台任务异常
            }

            gate.Dispose();
        }
    }
}

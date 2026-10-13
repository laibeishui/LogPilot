namespace LogPilot.Application.Common;

/// <summary>
/// 管理 API / JSON API 统一错误体（OTLP 端点不使用此结构）。
/// </summary>
public sealed class ApiError
{
    public required string Code { get; init; }

    public required string Message { get; init; }

    public required string RequestId { get; init; }

    public object? Details { get; init; }
}

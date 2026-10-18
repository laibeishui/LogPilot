namespace LogPilot.Application.Common;

/// <summary>
/// 可映射到稳定 HTTP 状态码的业务异常。
/// </summary>
public sealed class AppException : Exception
{
    public AppException(string code, string message, int statusCode = 400)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }

    public string Code { get; }

    public int StatusCode { get; }
}

/// <summary>
/// 日志存储（VictoriaLogs）不可用时的统一错误码与工厂方法。
/// 查询失败必须以异常上报，禁止降级成“0 条/空结果”，否则会出现虚假的“无数据”和错误的告警恢复。
/// </summary>
public static class LogStoreErrors
{
    public const string UnavailableCode = "LOG_STORE_UNAVAILABLE";

    public static AppException Unavailable(string operation)
        => new(UnavailableCode, $"日志存储暂时不可用（{operation}），请稍后重试。", 503);
}

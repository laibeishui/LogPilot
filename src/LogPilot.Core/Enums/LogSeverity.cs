namespace LogPilot.Core.Enums;

/// <summary>
/// 标准化日志严重级别（写入 VictoriaLogs 前统一为大写字符串）。
/// 命名避免与 Microsoft.Extensions.Logging.LogLevel 冲突。
/// </summary>
public enum LogSeverity
{
    Trace = 0,
    Debug = 1,
    Info = 2,
    Warn = 3,
    Error = 4,
    Fatal = 5
}

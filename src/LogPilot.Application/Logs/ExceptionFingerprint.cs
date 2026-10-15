using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace LogPilot.Application.Logs;

/// <summary>
/// 异常指纹：把"结构相同、仅参数不同"的错误归并为同一个问题。
/// 归一化消息中的可变片段（GUID、十六进制、IP、URL、数字）后计算稳定哈希。
/// </summary>
public static class ExceptionFingerprint
{
    private static readonly Regex GuidRegex = new(
        @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
        RegexOptions.Compiled);

    private static readonly Regex UrlRegex = new(@"https?://\S+", RegexOptions.Compiled);

    private static readonly Regex IpRegex = new(@"\b\d{1,3}(?:\.\d{1,3}){3}\b", RegexOptions.Compiled);

    private static readonly Regex HexRegex = new(@"\b[0-9a-fA-F]{8,}\b", RegexOptions.Compiled);

    private static readonly Regex NumberRegex = new(@"\b\d+\b", RegexOptions.Compiled);

    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    public static string Compute(string serviceName, string level, string? exceptionType, string message)
    {
        var raw = string.Join(
            '|',
            (serviceName ?? string.Empty).Trim().ToLowerInvariant(),
            (level ?? string.Empty).Trim().ToUpperInvariant(),
            (exceptionType ?? string.Empty).Trim(),
            Normalize(message));

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    /// <summary>
    /// 归一化异常/错误消息：去除随时间或请求变化的部分，仅保留结构。
    /// </summary>
    public static string Normalize(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        var text = message.Trim();
        text = UrlRegex.Replace(text, "<url>");
        text = GuidRegex.Replace(text, "<guid>");
        text = IpRegex.Replace(text, "<ip>");
        text = HexRegex.Replace(text, "<hex>");
        text = NumberRegex.Replace(text, "<n>");
        text = WhitespaceRegex.Replace(text, " ").Trim();

        return text.Length <= 300 ? text : text[..300];
    }
}

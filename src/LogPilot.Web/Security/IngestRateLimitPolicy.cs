using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using LogPilot.Application.Options;

namespace LogPilot.Web.Security;

/// <summary>
/// 日志接入端点的限流策略：按接入 Token（或来源 IP）分区，防止单个接入方刷爆进程内存与后端存储。
/// </summary>
public static class IngestRateLimitPolicy
{
    /// <summary>接入端点路径前缀：OTLP 主入口与 HTTP JSON 辅助入口。</summary>
    private static readonly string[] IngestPrefixes = ["/v1/logs", "/api/v1/ingest"];

    /// <summary>
    /// 仅为接入端点创建限流分区，其余请求（页面、管理接口）不受影响。
    /// </summary>
    public static RateLimitPartition<string> CreatePartition(HttpContext context, IngestionLimits limits)
    {
        if (limits.MaxRequestsPerMinute <= 0 || !IsIngestPath(context.Request.Path))
        {
            return RateLimitPartition.GetNoLimiter("unlimited");
        }

        return RateLimitPartition.GetFixedWindowLimiter(
            ResolveClientKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limits.MaxRequestsPerMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            });
    }

    private static bool IsIngestPath(PathString path)
    {
        foreach (var prefix in IngestPrefixes)
        {
            if (path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 优先按 Authorization 头中的接入 Token 分区（先哈希，避免明文密钥驻留内存），
    /// 未带 Token 时退化为按来源 IP 分区。
    /// </summary>
    private static string ResolveClientKey(HttpContext context)
    {
        var authorization = context.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(authorization))
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(authorization));
            return "token:" + Convert.ToHexString(hash);
        }

        return "ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
    }
}

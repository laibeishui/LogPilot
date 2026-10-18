using System.IO.Compression;
using LogPilot.Application.Common;
using LogPilot.Application.Ingestion;
using LogPilot.Application.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace LogPilot.Web.ApiControllers;

/// <summary>
/// OTLP/HTTP 日志接入（主入口）：POST /v1/logs
/// 支持 Protobuf（application/x-protobuf，默认）与 JSON（application/json）两种编码。
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("v1")]
public sealed class OtlpLogsApiController : ControllerBase
{
    private readonly IIngestApiKeyAuthenticator _authenticator;
    private readonly IOtlpLogIngestService _ingest;
    private readonly IngestionLimits _limits;

    public OtlpLogsApiController(
        IIngestApiKeyAuthenticator authenticator,
        IOtlpLogIngestService ingest,
        IOptions<IngestionLimits> limits)
    {
        _authenticator = authenticator;
        _ingest = ingest;
        _limits = limits.Value;
    }

    [HttpPost("logs")]
    [RequestSizeLimit(8_388_608)]
    public async Task<IActionResult> ExportLogs(CancellationToken cancellationToken)
    {
        try
        {
            var principal = await _authenticator.AuthenticateAsync(
                Request.Headers.Authorization.ToString(),
                cancellationToken);

            await using var bodyStream = OpenBodyStream();
            var bytes = await ReadWithinLimitAsync(bodyStream, _limits.MaxBodyBytes, cancellationToken)
                .ConfigureAwait(false);

            if (bytes is null)
            {
                return StatusCode(StatusCodes.Status413PayloadTooLarge);
            }

            var encoding = ResolveEncoding();

            var result = await _ingest.IngestAsync(principal, bytes, encoding, cancellationToken)
                .ConfigureAwait(false);

            Response.StatusCode = result.StatusCode;
            return File(result.ResponseBody, result.ContentType);
        }
        catch (AppException ex) when (ex.StatusCode == 401)
        {
            return Unauthorized();
        }
        catch (InvalidDataException)
        {
            return BadRequest();
        }
    }

    private OtlpEncoding ResolveEncoding()
    {
        var contentType = Request.ContentType ?? string.Empty;
        return contentType.Contains("json", StringComparison.OrdinalIgnoreCase)
            ? OtlpEncoding.Json
            : OtlpEncoding.Protobuf;
    }

    private Stream OpenBodyStream()
    {
        var encoding = Request.Headers.ContentEncoding.ToString();
        if (encoding.Contains("gzip", StringComparison.OrdinalIgnoreCase))
        {
            return new GZipStream(Request.Body, CompressionMode.Decompress, leaveOpen: true);
        }

        return Request.Body;
    }

    /// <summary>
    /// 在 maxBytes 限额内读取整个请求体（gzip 时为解压后的字节数），超限返回 null。
    /// 限额必须在读取过程中判断：若先全量解压再检查，构造出的压缩包可在极小体积下展开出极大内容（zip bomb）撑爆内存。
    /// </summary>
    private static async Task<byte[]?> ReadWithinLimitAsync(
        Stream body,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        var chunk = new byte[64 * 1024];

        int read;
        while ((read = await body.ReadAsync(chunk.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (memory.Length + read > maxBytes)
            {
                return null;
            }

            memory.Write(chunk, 0, read);
        }

        return memory.ToArray();
    }
}

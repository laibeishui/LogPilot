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

            await using var bodyStream = await OpenBodyStreamAsync(cancellationToken).ConfigureAwait(false);
            using var memory = new MemoryStream();
            await bodyStream.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
            var bytes = memory.ToArray();

            if (bytes.Length > _limits.MaxBodyBytes)
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

    private async Task<Stream> OpenBodyStreamAsync(CancellationToken cancellationToken)
    {
        var encoding = Request.Headers.ContentEncoding.ToString();
        if (encoding.Contains("gzip", StringComparison.OrdinalIgnoreCase))
        {
            // 解压后仍受 MaxBodyBytes 限制（由上层检查）
            var gzip = new GZipStream(Request.Body, CompressionMode.Decompress, leaveOpen: true);
            var limited = new MemoryStream();
            await gzip.CopyToAsync(limited, cancellationToken).ConfigureAwait(false);
            await gzip.DisposeAsync().ConfigureAwait(false);
            limited.Position = 0;
            return limited;
        }

        return Request.Body;
    }
}

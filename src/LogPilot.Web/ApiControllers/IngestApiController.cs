using LogPilot.Application.Common;
using LogPilot.Application.Ingestion;
using LogPilot.Application.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace LogPilot.Web.ApiControllers;

/// <summary>
/// HTTP JSON 批量日志接入（辅助入口）。
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/ingest")]
public sealed class IngestApiController : ControllerBase
{
    private readonly IIngestApiKeyAuthenticator _authenticator;
    private readonly IJsonLogIngestService _ingest;
    private readonly IngestionLimits _limits;

    public IngestApiController(
        IIngestApiKeyAuthenticator authenticator,
        IJsonLogIngestService ingest,
        IOptions<IngestionLimits> limits)
    {
        _authenticator = authenticator;
        _ingest = ingest;
        _limits = limits.Value;
    }

    [HttpPost("logs")]
    [RequestSizeLimit(2_097_152)]
    public async Task<IActionResult> IngestLogs(
        [FromBody] JsonIngestRequest request,
        CancellationToken cancellationToken)
    {
        if (Request.ContentLength is > 0 && Request.ContentLength > _limits.MaxBodyBytes)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge, new ApiError
            {
                Code = "PAYLOAD_TOO_LARGE",
                Message = $"请求体超过 {_limits.MaxBodyBytes} 字节。",
                RequestId = HttpContext.TraceIdentifier
            });
        }

        try
        {
            var principal = await _authenticator.AuthenticateAsync(
                Request.Headers.Authorization.ToString(),
                cancellationToken);

            var response = await _ingest.IngestAsync(
                principal,
                request,
                HttpContext.TraceIdentifier,
                cancellationToken);

            return Ok(response);
        }
        catch (AppException ex)
        {
            return StatusCode(ex.StatusCode, new ApiError
            {
                Code = ex.Code,
                Message = ex.Message,
                RequestId = HttpContext.TraceIdentifier
            });
        }
    }
}

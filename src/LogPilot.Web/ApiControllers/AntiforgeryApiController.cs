using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LogPilot.Web.ApiControllers;

[ApiController]
[Route("api/v1/antiforgery")]
public sealed class AntiforgeryApiController : ControllerBase
{
    private readonly IAntiforgery _antiforgery;

    public AntiforgeryApiController(IAntiforgery antiforgery)
    {
        _antiforgery = antiforgery;
    }

    /// <summary>
    /// 获取 CSRF 令牌（同时写入 Cookie）。登录页 / 初始化页可匿名调用。
    /// </summary>
    [AllowAnonymous]
    [HttpGet("token")]
    public IActionResult Token()
    {
        var tokens = _antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new { token = tokens.RequestToken });
    }
}

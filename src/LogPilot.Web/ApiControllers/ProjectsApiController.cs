using LogPilot.Application.Projects;
using LogPilot.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LogPilot.Web.ApiControllers;

[ApiController]
[Authorize]
[Route("api/v1/projects")]
public sealed class ProjectsApiController : ControllerBase
{
    private readonly IProjectService _projects;
    private readonly IApiKeyService _apiKeys;

    public ProjectsApiController(IProjectService projects, IApiKeyService apiKeys)
    {
        _projects = projects;
        _apiKeys = apiKeys;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var items = await _projects.ListAsync(userId, User.IsSystemAdmin(), cancellationToken);
        return Ok(items);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateProjectRequest request, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var project = await _projects.CreateAsync(userId, User.IsSystemAdmin(), request, cancellationToken);
        return Ok(project);
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(long id, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var project = await _projects.GetAsync(userId, User.IsSystemAdmin(), id, cancellationToken);
        return Ok(project);
    }

    [HttpPatch("{id:long}")]
    public async Task<IActionResult> Update(
        long id,
        [FromBody] UpdateProjectRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var project = await _projects.UpdateAsync(userId, User.IsSystemAdmin(), id, request, cancellationToken);
        return Ok(project);
    }

    [HttpGet("{id:long}/api-keys")]
    public async Task<IActionResult> ListKeys(long id, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var keys = await _apiKeys.ListAsync(userId, User.IsSystemAdmin(), id, cancellationToken);
        return Ok(keys);
    }

    [HttpPost("{id:long}/api-keys")]
    public async Task<IActionResult> CreateKey(
        long id,
        [FromBody] CreateApiKeyRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var key = await _apiKeys.CreateAsync(userId, User.IsSystemAdmin(), id, request, cancellationToken);
        return Ok(key);
    }

    [HttpDelete("{id:long}/api-keys/{keyId:long}")]
    public async Task<IActionResult> RevokeKey(long id, long keyId, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        await _apiKeys.RevokeAsync(userId, User.IsSystemAdmin(), id, keyId, cancellationToken);
        return Ok(new { ok = true });
    }
}

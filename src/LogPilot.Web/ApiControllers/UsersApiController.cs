using LogPilot.Application.Identity;
using LogPilot.Application.Projects;
using LogPilot.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LogPilot.Web.ApiControllers;

/// <summary>
/// 人员管理：账号增删改、密码重置、项目授权。仅系统管理员可用。
/// </summary>
[ApiController]
[Authorize(Policy = LogPilotPolicies.SystemAdmin)]
[Route("api/v1/users")]
public sealed class UsersApiController : ControllerBase
{
    private readonly IUserManagementService _users;
    private readonly IProjectMemberService _members;

    public UsersApiController(IUserManagementService users, IProjectMemberService members)
    {
        _users = users;
        _members = members;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? keyword,
        [FromQuery] int page,
        [FromQuery] int pageSize,
        CancellationToken cancellationToken)
    {
        var result = await _users.ListAsync(keyword, page, pageSize, cancellationToken);
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        var user = await _users.CreateAsync(request, cancellationToken);
        return Ok(user);
    }

    [HttpPatch("{id:long}")]
    public async Task<IActionResult> Update(
        long id,
        [FromBody] UpdateUserRequest request,
        CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var operatorUserId))
        {
            return Unauthorized();
        }

        var user = await _users.UpdateAsync(operatorUserId, id, request, cancellationToken);
        return Ok(user);
    }

    [HttpPost("{id:long}/password")]
    public async Task<IActionResult> ResetPassword(
        long id,
        [FromBody] ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        await _users.ResetPasswordAsync(id, request, cancellationToken);
        return Ok(new { ok = true });
    }

    [HttpGet("{id:long}/memberships")]
    public async Task<IActionResult> GetMemberships(long id, CancellationToken cancellationToken)
    {
        var items = await _members.ListByUserAsync(id, cancellationToken);
        return Ok(items);
    }

    [HttpPut("{id:long}/memberships")]
    public async Task<IActionResult> SetMemberships(
        long id,
        [FromBody] UserMembershipsRequest request,
        CancellationToken cancellationToken)
    {
        var items = await _members.ReplaceByUserAsync(id, request, cancellationToken);
        return Ok(items);
    }
}

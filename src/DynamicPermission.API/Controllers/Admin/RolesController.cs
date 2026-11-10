using DynamicPermission.API.Attributes;
using DynamicPermission.Application.Common.Interfaces;
using DynamicPermission.Application.Models.Admin;
using DynamicPermission.Application.Models.Admin.Permissions;
using DynamicPermission.Application.Models.Admin.Roles;
using Microsoft.AspNetCore.Mvc;

namespace DynamicPermission.API.Controllers.Admin;

[ApiController]
[Route("api/admin/roles")]
public sealed class RolesController : ControllerBase
{
    private readonly IRoleManagementService _roleService;

    public RolesController(IRoleManagementService roleService)
    {
        _roleService = roleService;
    }

    [HttpGet]
    [HasPermission("role.view")]
    [ProducesResponseType(typeof(PagedResponse<RoleResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<RoleResponse>>> GetRoles(
        [FromQuery] RoleListQuery query,
        CancellationToken cancellationToken)
    {
        return Ok(await _roleService.GetRolesAsync(query, cancellationToken));
    }

    [HttpGet("{id:guid}", Name = nameof(GetRoleById))]
    [HasPermission("role.view")]
    [ProducesResponseType(typeof(RoleResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RoleResponse>> GetRoleById(Guid id, CancellationToken cancellationToken)
    {
        var role = await _roleService.GetRoleAsync(id, cancellationToken);
        return role is null ? NotFound() : Ok(role);
    }

    [HttpPost]
    [HasPermission("role.create")]
    [ProducesResponseType(typeof(RoleResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RoleResponse>> CreateRole(
        CreateRoleRequest request,
        CancellationToken cancellationToken)
    {
        var role = await _roleService.CreateRoleAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetRoleById), new { id = role.Id }, role);
    }

    [HttpPut("{id:guid}")]
    [HasPermission("role.update")]
    [ProducesResponseType(typeof(RoleResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RoleResponse>> UpdateRole(
        Guid id,
        UpdateRoleRequest request,
        CancellationToken cancellationToken)
    {
        var role = await _roleService.UpdateRoleAsync(id, request, cancellationToken);
        return role is null ? NotFound() : Ok(role);
    }

    [HttpDelete("{id:guid}")]
    [HasPermission("role.deactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeactivateRole(Guid id, CancellationToken cancellationToken)
    {
        var role = await _roleService.DeactivateRoleAsync(id, cancellationToken);
        return role is null ? NotFound() : NoContent();
    }

    [HttpPut("{id:guid}/status")]
    [HasPermission("role.deactivate")]
    [ProducesResponseType(typeof(RoleResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RoleResponse>> SetRoleStatus(
        Guid id,
        UpdateRoleStatusRequest request,
        CancellationToken cancellationToken)
    {
        var role = await _roleService.SetRoleStatusAsync(id, request, cancellationToken);
        return role is null ? NotFound() : Ok(role);
    }

    [HttpGet("{id:guid}/permissions")]
    [HasPermission("role.view")]
    [ProducesResponseType(typeof(PagedResponse<PermissionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResponse<PermissionResponse>>> GetRolePermissions(
        Guid id,
        [FromQuery] PermissionListQuery query,
        CancellationToken cancellationToken)
    {
        var permissions = await _roleService.GetRolePermissionsAsync(id, query, cancellationToken);
        return permissions is null ? NotFound() : Ok(permissions);
    }

    [HttpPut("{id:guid}/permissions/{permissionId:guid}")]
    [HasPermission("role.permission.assign")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AssignPermission(
        Guid id,
        Guid permissionId,
        CancellationToken cancellationToken)
    {
        var assigned = await _roleService.AssignPermissionAsync(id, permissionId, cancellationToken);
        return assigned ? NoContent() : NotFound();
    }

    [HttpDelete("{id:guid}/permissions/{permissionId:guid}")]
    [HasPermission("role.permission.assign")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemovePermission(
        Guid id,
        Guid permissionId,
        CancellationToken cancellationToken)
    {
        var removed = await _roleService.RemovePermissionAsync(id, permissionId, cancellationToken);
        return removed ? NoContent() : NotFound();
    }
}

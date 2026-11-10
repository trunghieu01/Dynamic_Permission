using DynamicPermission.API.Attributes;
using DynamicPermission.Application.Common.Interfaces;
using DynamicPermission.Application.Models.Admin;
using DynamicPermission.Application.Models.Admin.Permissions;
using Microsoft.AspNetCore.Mvc;

namespace DynamicPermission.API.Controllers.Admin;

[ApiController]
[Route("api/admin/permissions")]
public sealed class PermissionsController : ControllerBase
{
    private readonly IPermissionManagementService _permissionService;

    public PermissionsController(IPermissionManagementService permissionService)
    {
        _permissionService = permissionService;
    }

    [HttpGet]
    [HasPermission("permission.view")]
    [ProducesResponseType(typeof(PagedResponse<PermissionResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<PermissionResponse>>> GetPermissions(
        [FromQuery] PermissionListQuery query,
        CancellationToken cancellationToken)
    {
        return Ok(await _permissionService.GetPermissionsAsync(query, cancellationToken));
    }

    [HttpGet("{id:guid}", Name = nameof(GetPermissionById))]
    [HasPermission("permission.view")]
    [ProducesResponseType(typeof(PermissionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PermissionResponse>> GetPermissionById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var permission = await _permissionService.GetPermissionAsync(id, cancellationToken);
        return permission is null ? NotFound() : Ok(permission);
    }

    [HttpPost]
    [HasPermission("permission.create")]
    [ProducesResponseType(typeof(PermissionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PermissionResponse>> CreatePermission(
        CreatePermissionRequest request,
        CancellationToken cancellationToken)
    {
        var permission = await _permissionService.CreatePermissionAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetPermissionById), new { id = permission.Id }, permission);
    }

    [HttpPut("{id:guid}")]
    [HasPermission("permission.update")]
    [ProducesResponseType(typeof(PermissionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PermissionResponse>> UpdatePermission(
        Guid id,
        UpdatePermissionRequest request,
        CancellationToken cancellationToken)
    {
        var permission = await _permissionService.UpdatePermissionAsync(id, request, cancellationToken);
        return permission is null ? NotFound() : Ok(permission);
    }

    [HttpPut("{id:guid}/status")]
    [HasPermission("permission.deactivate")]
    [ProducesResponseType(typeof(PermissionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PermissionResponse>> SetPermissionStatus(
        Guid id,
        UpdatePermissionStatusRequest request,
        CancellationToken cancellationToken)
    {
        var permission = await _permissionService.SetPermissionStatusAsync(id, request, cancellationToken);
        return permission is null ? NotFound() : Ok(permission);
    }
}

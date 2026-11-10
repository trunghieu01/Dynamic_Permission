using DynamicPermission.API.Attributes;
using DynamicPermission.Application.Common.Interfaces;
using DynamicPermission.Application.Models.Admin;
using DynamicPermission.Application.Models.Admin.Permissions;
using DynamicPermission.Application.Models.Admin.Roles;
using DynamicPermission.Application.Models.Admin.Users;
using Microsoft.AspNetCore.Mvc;

namespace DynamicPermission.API.Controllers.Admin;

[ApiController]
[Route("api/admin/users")]
public sealed class UsersController : ControllerBase
{
    private readonly IUserManagementService _userService;

    public UsersController(IUserManagementService userService)
    {
        _userService = userService;
    }

    [HttpGet]
    [HasPermission("user.view")]
    [ProducesResponseType(typeof(PagedResponse<UserResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<UserResponse>>> GetUsers(
        [FromQuery] UserListQuery query,
        CancellationToken cancellationToken)
    {
        return Ok(await _userService.GetUsersAsync(query, cancellationToken));
    }

    [HttpGet("{id:guid}", Name = nameof(GetUserById))]
    [HasPermission("user.view")]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserResponse>> GetUserById(Guid id, CancellationToken cancellationToken)
    {
        var user = await _userService.GetUserAsync(id, cancellationToken);
        return user is null ? NotFound() : Ok(user);
    }

    [HttpPost]
    [HasPermission("user.create")]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserResponse>> CreateUser(
        CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        var user = await _userService.CreateUserAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetUserById), new { id = user.Id }, user);
    }

    [HttpPut("{id:guid}")]
    [HasPermission("user.update")]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserResponse>> UpdateUser(
        Guid id,
        UpdateUserRequest request,
        CancellationToken cancellationToken)
    {
        var user = await _userService.UpdateUserAsync(id, request, cancellationToken);
        return user is null ? NotFound() : Ok(user);
    }

    [HttpPut("{id:guid}/status")]
    [HasPermission("user.lock")]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserResponse>> SetUserStatus(
        Guid id,
        UpdateUserStatusRequest request,
        CancellationToken cancellationToken)
    {
        var user = await _userService.SetUserStatusAsync(id, request, cancellationToken);
        return user is null ? NotFound() : Ok(user);
    }

    [HttpPut("{id:guid}/role")]
    [HasPermission("user.role.assign")]
    [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserResponse>> AssignRole(
        Guid id,
        AssignUserRoleRequest request,
        CancellationToken cancellationToken)
    {
        var user = await _userService.AssignRoleAsync(id, request, cancellationToken);
        return user is null ? NotFound() : Ok(user);
    }

    [HttpGet("{id:guid}/roles")]
    [HasPermission("user.view")]
    [ProducesResponseType(typeof(PagedResponse<RoleResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResponse<RoleResponse>>> GetUserRoles(
        Guid id,
        [FromQuery] RoleListQuery query,
        CancellationToken cancellationToken)
    {
        var roles = await _userService.GetUserRolesAsync(id, query, cancellationToken);
        return roles is null ? NotFound() : Ok(roles);
    }

    [HttpGet("{id:guid}/effective-permissions")]
    [HasPermission("user.permissions.view")]
    [ProducesResponseType(typeof(PagedResponse<PermissionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResponse<PermissionResponse>>> GetEffectivePermissions(
        Guid id,
        [FromQuery] PermissionListQuery query,
        CancellationToken cancellationToken)
    {
        var permissions = await _userService.GetEffectivePermissionsAsync(id, query, cancellationToken);
        return permissions is null ? NotFound() : Ok(permissions);
    }
}

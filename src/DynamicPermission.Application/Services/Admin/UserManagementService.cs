using DynamicPermission.Application.Common.Interfaces;
using DynamicPermission.Application.Common.Exceptions;
using DynamicPermission.Application.Models.Admin;
using DynamicPermission.Application.Models.Admin.Permissions;
using DynamicPermission.Application.Models.Admin.Roles;
using DynamicPermission.Application.Models.Admin.Users;
using DynamicPermission.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DynamicPermission.Application.Services.Admin;

public sealed class UserManagementService : IUserManagementService
{
    private readonly IApplicationDbContext _dbContext;
    private readonly IPasswordHasherService _passwordHasher;
    private readonly IPermissionService _permissionService;

    public UserManagementService(
        IApplicationDbContext dbContext,
        IPasswordHasherService passwordHasher,
        IPermissionService permissionService)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _permissionService = permissionService;
    }

    public async Task<PagedResponse<UserResponse>> GetUsersAsync(
        UserListQuery query,
        CancellationToken cancellationToken)
    {
        var users = _dbContext.Users.AsNoTracking().Include(user => user.Role).AsQueryable();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            users = users.Where(user =>
                user.Username.Contains(search)
                || user.Email.Contains(search)
                || user.FullName.Contains(search));
        }

        if (query.IsActive.HasValue)
        {
            users = users.Where(user => user.IsActive == query.IsActive.Value);
        }

        if (query.RoleId.HasValue)
        {
            users = users.Where(user => user.RoleId == query.RoleId.Value);
        }

        users = AdminQueryHelper.ApplyUserSort(users, query);
        return await AdminQueryHelper.ToPagedResponseAsync(users, query, MapUser, cancellationToken);
    }

    public async Task<UserResponse?> GetUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users.AsNoTracking()
            .Include(candidate => candidate.Role)
            .SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);
        return user is null ? null : MapUser(user);
    }

    public async Task<UserResponse> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        var username = request.Username.Trim();
        var email = request.Email.Trim();
        if (await _dbContext.Users.AnyAsync(user =>
                user.Username.ToUpper() == username.ToUpper()
                || user.Email.ToUpper() == email.ToUpper(),
                cancellationToken))
        {
            throw new ConflictException("Username or email is already in use.");
        }

        var role = await _dbContext.Roles.SingleOrDefaultAsync(candidate => candidate.Id == request.RoleId, cancellationToken)
            ?? throw new KeyNotFoundException("Role was not found.");
        if (!role.IsActive)
        {
            throw new ConflictException("An inactive role cannot be assigned.");
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            RoleId = role.Id,
            Role = role,
            Username = username,
            Email = email,
            FullName = request.FullName.Trim(),
            IsActive = true
        };
        user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return MapUser(user);
    }

    public async Task<UserResponse?> UpdateUserAsync(
        Guid userId,
        UpdateUserRequest request,
        CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users.Include(candidate => candidate.Role)
            .SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var username = request.Username.Trim();
        var email = request.Email.Trim();
        if (await _dbContext.Users.AnyAsync(candidate =>
                candidate.Id != userId
                && (candidate.Username.ToUpper() == username.ToUpper()
                    || candidate.Email.ToUpper() == email.ToUpper()),
                cancellationToken))
        {
            throw new ConflictException("Username or email is already in use.");
        }

        user.Username = username;
        user.Email = email;
        user.FullName = request.FullName.Trim();
        await _dbContext.SaveChangesAsync(cancellationToken);
        return MapUser(user);
    }

    public async Task<UserResponse?> SetUserStatusAsync(
        Guid userId,
        UpdateUserStatusRequest request,
        CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users.Include(candidate => candidate.Role)
            .SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);
        if (user is null)
        {
            return null;
        }

        user.IsActive = request.IsActive
            ?? throw new ArgumentException("IsActive is required.", nameof(request));
        await _dbContext.SaveChangesAsync(cancellationToken);
        return MapUser(user);
    }

    public async Task<UserResponse?> AssignRoleAsync(
        Guid userId,
        AssignUserRoleRequest request,
        CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users.Include(candidate => candidate.Role)
            .SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var role = await _dbContext.Roles.SingleOrDefaultAsync(candidate => candidate.Id == request.RoleId, cancellationToken)
            ?? throw new KeyNotFoundException("Role was not found.");
        if (!role.IsActive)
        {
            throw new ConflictException("An inactive role cannot be assigned.");
        }

        user.RoleId = role.Id;
        user.Role = role;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return MapUser(user);
    }

    public async Task<PagedResponse<RoleResponse>?> GetUserRolesAsync(
        Guid userId,
        RoleListQuery query,
        CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var roles = _dbContext.Roles.AsNoTracking().Where(role => role.Id == user.RoleId);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            roles = roles.Where(role => role.Code.Contains(search) || role.Name.Contains(search));
        }

        if (query.IsActive.HasValue)
        {
            roles = roles.Where(role => role.IsActive == query.IsActive.Value);
        }

        roles = AdminQueryHelper.ApplyRoleSort(roles, query);
        return await AdminQueryHelper.ToPagedResponseAsync(roles, query, RoleManagementService.MapRole, cancellationToken);
    }

    public async Task<PagedResponse<PermissionResponse>?> GetEffectivePermissionsAsync(
        Guid userId,
        PermissionListQuery query,
        CancellationToken cancellationToken)
    {
        var exists = await _dbContext.Users.AsNoTracking()
            .AnyAsync(user => user.Id == userId, cancellationToken);
        if (!exists)
        {
            return null;
        }

        var effectiveCodes = await _permissionService.GetUserEffectivePermissionsAsync(userId, cancellationToken);
        var permissions = _dbContext.Permissions.AsNoTracking()
            .Where(permission => effectiveCodes.Contains(permission.Code));
        permissions = AdminQueryHelper.FilterPermissions(permissions, query);
        permissions = AdminQueryHelper.ApplyPermissionSort(permissions, query);
        return await AdminQueryHelper.ToPagedResponseAsync(permissions, query, PermissionManagementService.MapPermission, cancellationToken);
    }

    private static UserResponse MapUser(User user) => new(
        user.Id,
        user.Username,
        user.Email,
        user.FullName,
        user.IsActive,
        user.RoleId,
        user.Role.Code,
        user.Role.Name,
        user.CreatedAt,
        user.UpdatedAt);
}

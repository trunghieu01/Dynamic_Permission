using DynamicPermission.Application.Services;
using DynamicPermission.Domain.Entities;
using DynamicPermission.Infrastructure.Authorization;
using DynamicPermission.Infrastructure.Persistence;
using DynamicPermission.UnitTests.Common;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;

namespace DynamicPermission.UnitTests.Application.Services;

public class PermissionServiceTests
{
    private readonly DynamicPermissionDbContext _context;
    private readonly MemoryPermissionCache _permissionCache;
    private readonly PermissionService _permissionService;

    public PermissionServiceTests()
    {
        _permissionCache = new MemoryPermissionCache(new MemoryCache(new MemoryCacheOptions()));
        _context = TestDbContextFactory.Create(permissionCache: _permissionCache);
        _permissionService = new PermissionService(_context, _permissionCache);
    }

    [Fact]
    public async Task GetUserEffectivePermissions_ShouldReturnRolePermissions_WhenUserAndRoleAreActive()
    {
        var role = TestDataBuilder.CreateRole(isActive: true);
        var user = TestDataBuilder.CreateUser(role.Id, isActive: true);
        var view = CreatePermission("customer.view");
        var create = CreatePermission("customer.create");
        AddEntities(role, user, view, create);
        _context.RolePermissions.AddRange(
            CreateRolePermission(role, view),
            CreateRolePermission(role, create));
        await _context.SaveChangesAsync();

        var result = await _permissionService.GetUserEffectivePermissionsAsync(user.Id);

        result.Should().BeEquivalentTo("customer.view", "customer.create");
    }

    [Fact]
    public async Task GetUserEffectivePermissions_ShouldReturnEmpty_WhenUserIsInactive()
    {
        var role = TestDataBuilder.CreateRole();
        var user = TestDataBuilder.CreateUser(role.Id, isActive: false);
        var view = CreatePermission("customer.view");
        AddEntities(role, user, view);
        _context.RolePermissions.Add(CreateRolePermission(role, view));
        await _context.SaveChangesAsync();

        var result = await _permissionService.GetUserEffectivePermissionsAsync(user.Id);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUserEffectivePermissions_ShouldExcludeRolePermissions_WhenRoleIsInactive()
    {
        var role = TestDataBuilder.CreateRole(isActive: false);
        var user = TestDataBuilder.CreateUser(role.Id);
        var view = CreatePermission("customer.view");
        AddEntities(role, user, view);
        _context.RolePermissions.Add(CreateRolePermission(role, view));
        await _context.SaveChangesAsync();

        var result = await _permissionService.GetUserEffectivePermissionsAsync(user.Id);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUserEffectivePermissions_ShouldIncludeDirectGrant_EvenIfRoleDoesNotHaveIt()
    {
        var role = TestDataBuilder.CreateRole();
        var user = TestDataBuilder.CreateUser(role.Id);
        var export = CreatePermission("customer.export");
        AddEntities(role, user, export);
        _context.UserPermissions.Add(CreateUserPermission(user, export, isGranted: true));
        await _context.SaveChangesAsync();

        var result = await _permissionService.GetUserEffectivePermissionsAsync(user.Id);

        result.Should().Contain("customer.export");
    }

    [Fact]
    public async Task GetUserEffectivePermissions_ShouldExcludeDirectDeny_EvenIfRoleHasIt()
    {
        var role = TestDataBuilder.CreateRole();
        var user = TestDataBuilder.CreateUser(role.Id);
        var delete = CreatePermission("customer.delete");
        AddEntities(role, user, delete);
        _context.RolePermissions.Add(CreateRolePermission(role, delete));
        _context.UserPermissions.Add(CreateUserPermission(user, delete, isGranted: false));
        await _context.SaveChangesAsync();

        var result = await _permissionService.GetUserEffectivePermissionsAsync(user.Id);

        result.Should().NotContain("customer.delete");
    }

    [Fact]
    public async Task GetUserEffectivePermissions_ShouldRefreshCachedPermissions_AfterDatabaseChanges()
    {
        var role = TestDataBuilder.CreateRole();
        var user = TestDataBuilder.CreateUser(role.Id);
        var view = CreatePermission("customer.view");
        AddEntities(role, user, view);
        await _context.SaveChangesAsync();

        (await _permissionService.GetUserEffectivePermissionsAsync(user.Id)).Should().BeEmpty();

        _context.RolePermissions.Add(CreateRolePermission(role, view));
        await _context.SaveChangesAsync();

        var updatedPermissions = await _permissionService.GetUserEffectivePermissionsAsync(user.Id);

        updatedPermissions.Should().Contain("customer.view");
    }

    private void AddEntities(Role role, User user, params Permission[] permissions)
    {
        _context.Roles.Add(role);
        _context.Users.Add(user);
        _context.Permissions.AddRange(permissions);
    }

    private static Permission CreatePermission(string code)
    {
        return new Permission
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = code,
            Module = "customer",
            Action = code["customer.".Length..]
        };
    }

    private static RolePermission CreateRolePermission(Role role, Permission permission)
    {
        return new RolePermission { RoleId = role.Id, PermissionId = permission.Id };
    }

    private static UserPermission CreateUserPermission(User user, Permission permission, bool isGranted)
    {
        return new UserPermission { UserId = user.Id, PermissionId = permission.Id, IsGranted = isGranted };
    }
}

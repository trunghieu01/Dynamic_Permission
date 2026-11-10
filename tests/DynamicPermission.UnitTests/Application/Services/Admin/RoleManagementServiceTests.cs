using DynamicPermission.Application.Models.Admin.Permissions;
using DynamicPermission.Application.Common.Exceptions;
using DynamicPermission.Application.Models.Admin.Roles;
using DynamicPermission.Application.Services.Admin;
using DynamicPermission.Domain.Entities;
using DynamicPermission.Infrastructure.Persistence;
using DynamicPermission.UnitTests.Common;
using FluentAssertions;

namespace DynamicPermission.UnitTests.Application.Services.Admin;

public class RoleManagementServiceTests
{
    private readonly DynamicPermissionDbContext _context = TestDbContextFactory.Create();
    private readonly RoleManagementService _service;

    public RoleManagementServiceTests()
    {
        _service = new RoleManagementService(_context);
    }

    [Fact]
    public async Task CreateRoleAsync_ShouldRejectDuplicateCodeIgnoringCase()
    {
        _context.Roles.Add(TestDataBuilder.CreateRole("SUPPORT", "Support"));
        await _context.SaveChangesAsync();

        var action = () => _service.CreateRoleAsync(new CreateRoleRequest
        {
            Code = "support",
            Name = "Duplicate"
        }, CancellationToken.None);

        await action.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task DeactivateRoleAsync_ShouldKeepRoleButDisableIt()
    {
        var role = TestDataBuilder.CreateRole();
        _context.Roles.Add(role);
        await _context.SaveChangesAsync();

        var result = await _service.DeactivateRoleAsync(role.Id, CancellationToken.None);

        result!.IsActive.Should().BeFalse();
        _context.Roles.Single().IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task SetRoleStatusAsync_ShouldAllowReactivation()
    {
        var role = TestDataBuilder.CreateRole(isActive: false);
        _context.Roles.Add(role);
        await _context.SaveChangesAsync();

        var result = await _service.SetRoleStatusAsync(role.Id, true, CancellationToken.None);

        result!.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task AssignPermissionAsync_ShouldRejectDuplicateAssignment()
    {
        var role = TestDataBuilder.CreateRole();
        var permission = CreatePermission("report.view");
        _context.Roles.Add(role);
        _context.Permissions.Add(permission);
        _context.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id });
        await _context.SaveChangesAsync();

        var action = () => _service.AssignPermissionAsync(role.Id, permission.Id, CancellationToken.None);

        await action.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task AssignPermissionAsync_ShouldRejectInactivePermission()
    {
        var role = TestDataBuilder.CreateRole();
        var permission = CreatePermission("report.view", isActive: false);
        _context.Roles.Add(role);
        _context.Permissions.Add(permission);
        await _context.SaveChangesAsync();

        var action = () => _service.AssignPermissionAsync(role.Id, permission.Id, CancellationToken.None);

        await action.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task GetRolePermissionsAsync_ShouldFilterAndPaginateAssignments()
    {
        var role = TestDataBuilder.CreateRole();
        var view = CreatePermission("report.view", module: "report");
        var export = CreatePermission("report.export", module: "report");
        var unrelated = CreatePermission("user.view", module: "user");
        _context.Roles.Add(role);
        _context.Permissions.AddRange(view, export, unrelated);
        _context.RolePermissions.AddRange(
            new RolePermission { RoleId = role.Id, PermissionId = view.Id },
            new RolePermission { RoleId = role.Id, PermissionId = export.Id },
            new RolePermission { RoleId = role.Id, PermissionId = unrelated.Id });
        await _context.SaveChangesAsync();

        var result = await _service.GetRolePermissionsAsync(role.Id, new PermissionListQuery
        {
            Module = "report",
            Page = 1,
            PageSize = 1,
            SortBy = "code"
        }, CancellationToken.None);

        result!.TotalCount.Should().Be(2);
        result.Items.Should().ContainSingle().Which.Code.Should().Be("report.export");
    }

    private static Permission CreatePermission(string code, string module = "report", bool isActive = true)
    {
        return new Permission
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = code,
            Module = module,
            Action = code.Split('.').Last(),
            IsActive = isActive
        };
    }
}

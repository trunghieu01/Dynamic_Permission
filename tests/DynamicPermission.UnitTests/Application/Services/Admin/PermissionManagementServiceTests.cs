using DynamicPermission.Application.Models.Admin.Permissions;
using DynamicPermission.Application.Common.Exceptions;
using DynamicPermission.Application.Services.Admin;
using DynamicPermission.Domain.Entities;
using DynamicPermission.Infrastructure.Persistence;
using DynamicPermission.UnitTests.Common;
using FluentAssertions;

namespace DynamicPermission.UnitTests.Application.Services.Admin;

public class PermissionManagementServiceTests
{
    private readonly DynamicPermissionDbContext _context = TestDbContextFactory.Create();
    private readonly PermissionManagementService _service;

    public PermissionManagementServiceTests()
    {
        _service = new PermissionManagementService(_context);
    }

    [Fact]
    public async Task CreatePermissionAsync_ShouldRejectDuplicateCodeIgnoringCase()
    {
        _context.Permissions.Add(CreatePermission("inventory.view"));
        await _context.SaveChangesAsync();

        var action = () => _service.CreatePermissionAsync(new CreatePermissionRequest
        {
            Code = "INVENTORY.VIEW",
            Name = "Duplicate",
            Module = "inventory",
            Action = "view"
        }, CancellationToken.None);

        await action.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task GetPermissionsAsync_ShouldApplyModuleFilter_AndNotAssumeActiveOnly()
    {
        _context.Permissions.AddRange(
            CreatePermission("inventory.view", "inventory", true),
            CreatePermission("inventory.legacy", "inventory", false),
            CreatePermission("user.view", "user", true));
        await _context.SaveChangesAsync();

        var result = await _service.GetPermissionsAsync(new PermissionListQuery
        {
            Module = "inventory",
            Page = 1,
            PageSize = 20
        }, CancellationToken.None);

        result.TotalCount.Should().Be(2);
        result.Items.Select(permission => permission.IsActive).Should().Contain(false);
    }

    [Fact]
    public async Task SetPermissionStatusAsync_ShouldUpdateStatus()
    {
        var permission = CreatePermission("inventory.view", "inventory", true);
        _context.Permissions.Add(permission);
        await _context.SaveChangesAsync();

        var result = await _service.SetPermissionStatusAsync(
            permission.Id,
            new UpdatePermissionStatusRequest { IsActive = false },
            CancellationToken.None);

        result!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task GetPermissionsAsync_ShouldRejectUnsupportedSortField()
    {
        var action = () => _service.GetPermissionsAsync(new PermissionListQuery
        {
            SortBy = "sql",
            Page = 1,
            PageSize = 20
        }, CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>();
    }

    private static Permission CreatePermission(string code, string module = "inventory", bool active = true)
    {
        return new Permission
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = code,
            Module = module,
            Action = code.Split('.').Last(),
            IsActive = active
        };
    }
}

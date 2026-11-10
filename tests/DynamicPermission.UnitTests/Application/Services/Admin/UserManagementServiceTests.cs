using DynamicPermission.Application.Common.Interfaces;
using DynamicPermission.Application.Common.Exceptions;
using DynamicPermission.Application.Models.Admin.Users;
using DynamicPermission.Application.Services.Admin;
using DynamicPermission.Domain.Entities;
using DynamicPermission.Infrastructure.Persistence;
using DynamicPermission.UnitTests.Common;
using FluentAssertions;
using Moq;

namespace DynamicPermission.UnitTests.Application.Services.Admin;

public class UserManagementServiceTests
{
    private readonly DynamicPermissionDbContext _context = TestDbContextFactory.Create();
    private readonly Mock<IPasswordHasherService> _passwordHasher = new();
    private readonly Mock<IPermissionService> _permissionService = new();
    private readonly UserManagementService _service;

    public UserManagementServiceTests()
    {
        _service = new UserManagementService(_context, _passwordHasher.Object, _permissionService.Object);
    }

    [Fact]
    public async Task CreateUserAsync_ShouldHashPassword_AndNeverReturnPasswordHash()
    {
        var role = TestDataBuilder.CreateRole();
        _context.Roles.Add(role);
        await _context.SaveChangesAsync();
        _passwordHasher.Setup(hasher => hasher.HashPassword(It.IsAny<User>(), "Password123"))
            .Returns("hashed-password");

        var result = await _service.CreateUserAsync(new CreateUserRequest
        {
            Username = "new.user",
            Email = "new@example.com",
            FullName = "New User",
            Password = "Password123",
            RoleId = role.Id
        }, CancellationToken.None);

        result.Username.Should().Be("new.user");
        result.Should().NotBeNull();
        result.GetType().GetProperties().Should().NotContain(property => property.Name == "PasswordHash");
        _context.Users.Single().PasswordHash.Should().Be("hashed-password");
    }

    [Fact]
    public async Task CreateUserAsync_ShouldRejectDuplicateUsernameIgnoringCase()
    {
        var role = TestDataBuilder.CreateRole();
        var existing = TestDataBuilder.CreateUser(role.Id, username: "Existing");
        _context.Roles.Add(role);
        _context.Users.Add(existing);
        await _context.SaveChangesAsync();

        var action = () => _service.CreateUserAsync(new CreateUserRequest
        {
            Username = "existing",
            Email = "other@example.com",
            FullName = "Other User",
            Password = "Password123",
            RoleId = role.Id
        }, CancellationToken.None);

        await action.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task CreateUserAsync_ShouldRejectInactiveRole()
    {
        var role = TestDataBuilder.CreateRole(isActive: false);
        _context.Roles.Add(role);
        await _context.SaveChangesAsync();

        var action = () => _service.CreateUserAsync(new CreateUserRequest
        {
            Username = "new.user",
            Email = "new@example.com",
            FullName = "New User",
            Password = "Password123",
            RoleId = role.Id
        }, CancellationToken.None);

        await action.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task GetUsersAsync_ShouldApplyOptionalFiltersAndPagination()
    {
        var role = TestDataBuilder.CreateRole();
        _context.Roles.Add(role);
        _context.Users.AddRange(
            TestDataBuilder.CreateUser(role.Id, username: "alpha", isActive: true),
            TestDataBuilder.CreateUser(role.Id, username: "beta", isActive: false),
            TestDataBuilder.CreateUser(role.Id, username: "alphabet", isActive: true));
        await _context.SaveChangesAsync();

        var page = await _service.GetUsersAsync(new UserListQuery
        {
            Search = "alpha",
            IsActive = true,
            Page = 1,
            PageSize = 1,
            SortBy = "username",
            SortDirection = "asc"
        }, CancellationToken.None);

        page.TotalCount.Should().Be(2);
        page.Items.Should().ContainSingle();
        page.Items[0].Username.Should().Be("alpha");
        page.TotalPages.Should().Be(2);
    }

    [Fact]
    public async Task AssignRoleAsync_ShouldReplaceRoleWithoutAllowingRolelessUser()
    {
        var oldRole = TestDataBuilder.CreateRole("OLD", "Old role");
        var newRole = TestDataBuilder.CreateRole("NEW", "New role");
        var user = TestDataBuilder.CreateUser(oldRole.Id);
        user.Role = oldRole;
        _context.Roles.AddRange(oldRole, newRole);
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var result = await _service.AssignRoleAsync(
            user.Id,
            new AssignUserRoleRequest { RoleId = newRole.Id },
            CancellationToken.None);

        result!.RoleId.Should().Be(newRole.Id);
        _context.Users.Single().RoleId.Should().Be(newRole.Id);
    }

    [Fact]
    public async Task GetUserRolesAsync_ShouldHonorFiltersWithoutReturningOtherUserRoles()
    {
        var role = TestDataBuilder.CreateRole("STAFF", "Staff");
        var otherRole = TestDataBuilder.CreateRole("OTHER", "Other");
        var user = TestDataBuilder.CreateUser(role.Id);
        _context.Roles.AddRange(role, otherRole);
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var roles = await _service.GetUserRolesAsync(
            user.Id,
            new DynamicPermission.Application.Models.Admin.Roles.RoleListQuery
            {
                Search = "STAFF",
                Page = 1,
                PageSize = 20
            },
            CancellationToken.None);

        roles!.TotalCount.Should().Be(1);
        roles.Items.Should().ContainSingle().Which.Code.Should().Be("STAFF");
    }
}

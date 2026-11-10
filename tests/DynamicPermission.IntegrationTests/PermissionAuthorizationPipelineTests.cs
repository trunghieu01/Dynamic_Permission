using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DynamicPermission.Application.Common.Interfaces;
using DynamicPermission.Application;
using DynamicPermission.Domain.Entities;
using DynamicPermission.Infrastructure;
using DynamicPermission.Infrastructure.Authentication;
using DynamicPermission.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DynamicPermission.IntegrationTests;

public sealed class PermissionAuthorizationPipelineTests
{
    [Fact]
    public async Task RegisterAndLogin_ShouldReturnUsableJwt_WithoutPasswordHash()
    {
        await using var host = await PermissionApiHost.StartAsync();
        var (adminId, adminRoleId) = await host.SeedUserAsync(
            "adminuser",
            "Admin!Password123",
            true,
            "user.create");
        var token = await host.LoginAsync("adminuser", "Admin!Password123");
        var staffRoleId = await AddRoleAsync(host, "STAFF");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register")
        {
            Content = JsonContent.Create(new
            {
                username = "newstaff",
                email = "newstaff@example.test",
                fullName = "New Staff",
                roleId = staffRoleId,
                password = "Staff!Password123"
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var registerResponse = await host.Client.SendAsync(request);
        registerResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var registerBody = await registerResponse.Content.ReadAsStringAsync();
        registerBody.Should().NotContain("PasswordHash");

        using var loginResponse = await host.Client.PostAsJsonAsync(
            "/api/auth/login",
            new { usernameOrEmail = "newstaff", password = "Staff!Password123" });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginJson = await loginResponse.Content.ReadAsStringAsync();
        loginJson.Should().NotContain("PasswordHash");
        using var document = JsonDocument.Parse(loginJson);
        document.RootElement.GetProperty("accessToken").GetString().Should().NotBeNullOrWhiteSpace();

        adminId.Should().NotBeEmpty();
        adminRoleId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task JwtPipeline_ShouldReturn401ForMissingOrInvalidToken_And403ForMissingPermission()
    {
        await using var host = await PermissionApiHost.StartAsync();
        var (userId, _) = await host.SeedUserAsync("reader", "Reader!Password123");

        using var anonymousResponse = await host.Client.GetAsync("/api/demo/customers");
        anonymousResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var invalidRequest = new HttpRequestMessage(HttpMethod.Get, "/api/demo/customers");
        invalidRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "forged.jwt.token");
        using var invalidResponse = await host.Client.SendAsync(invalidRequest);
        invalidResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var token = await host.LoginAsync("reader", "Reader!Password123");
        using var deniedResponse = await host.SendAuthorizedAsync(
            HttpMethod.Get,
            "/api/demo/customers",
            token);
        deniedResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        userId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task LockedUser_ShouldLosePermissionWhileExistingJwtRemainsValid()
    {
        await using var host = await PermissionApiHost.StartAsync();
        var (userId, _) = await host.SeedUserAsync(
            "lockedlater",
            "User!Password123",
            true,
            "customer.view",
            "user.lock");
        var token = await host.LoginAsync("lockedlater", "User!Password123");

        using var allowed = await host.SendAuthorizedAsync(HttpMethod.Get, "/api/demo/customers", token);
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);

        using var lockResponse = await host.SendAuthorizedAsync(
            HttpMethod.Put,
            $"/api/admin/users/{userId}/status",
            token,
            new { isActive = false });
        lockResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var denied = await host.SendAuthorizedAsync(HttpMethod.Get, "/api/demo/customers", token);
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeactivatedRole_ShouldRevokeDirectGrantForAnAlreadyAuthenticatedUser()
    {
        await using var host = await PermissionApiHost.StartAsync();
        var (userId, roleId) = await host.SeedUserAsync(
            "rolelocked",
            "User!Password123",
            true,
            "role.deactivate");
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DynamicPermissionDbContext>();
            var permission = PermissionApiHost.CreatePermission("customer.view");
            db.Permissions.Add(permission);
            db.UserPermissions.Add(new UserPermission
            {
                UserId = userId,
                PermissionId = permission.Id,
                IsGranted = true
            });
            await db.SaveChangesAsync();
        }

        var token = await host.LoginAsync("rolelocked", "User!Password123");
        using var allowed = await host.SendAuthorizedAsync(HttpMethod.Get, "/api/demo/customers", token);
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);

        using var deactivateResponse = await host.SendAuthorizedAsync(
            HttpMethod.Put,
            $"/api/admin/roles/{roleId}/status",
            token,
            new { isActive = false });
        deactivateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var denied = await host.SendAuthorizedAsync(HttpMethod.Get, "/api/demo/customers", token);
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RevokedPermission_ShouldTakeEffectWithoutReissuingJwt()
    {
        await using var host = await PermissionApiHost.StartAsync();
        var (_, roleId) = await host.SeedUserAsync(
            "revoked",
            "User!Password123",
            true,
            "customer.view",
            "role.permission.assign");
        var token = await host.LoginAsync("revoked", "User!Password123");
        using var allowed = await host.SendAuthorizedAsync(HttpMethod.Get, "/api/demo/customers", token);
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);

        Guid permissionId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DynamicPermissionDbContext>();
            permissionId = await db.Permissions
                .Where(permission => permission.Code == "customer.view")
                .Select(permission => permission.Id)
                .SingleAsync();
        }

        using var removeResponse = await host.SendAuthorizedAsync(
            HttpMethod.Delete,
            $"/api/admin/roles/{roleId}/permissions/{permissionId}",
            token);
        removeResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var denied = await host.SendAuthorizedAsync(HttpMethod.Get, "/api/demo/customers", token);
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RolePermissionUpdate_ShouldRefreshEffectivePermissionsForCurrentJwt()
    {
        await using var host = await PermissionApiHost.StartAsync();
        var (_, roleId) = await host.SeedUserAsync(
            "roleupdate",
            "User!Password123",
            true,
            "customer.view",
            "role.permission.assign");
        var token = await host.LoginAsync("roleupdate", "User!Password123");
        Guid viewPermissionId;
        Guid createPermissionId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DynamicPermissionDbContext>();
            viewPermissionId = await db.Permissions
                .Where(permission => permission.Code == "customer.view")
                .Select(permission => permission.Id)
                .SingleAsync();
            var createPermission = PermissionApiHost.CreatePermission("customer.create");
            db.Permissions.Add(createPermission);
            await db.SaveChangesAsync();
            createPermissionId = createPermission.Id;
        }

        using var beforeUpdate = await host.SendAuthorizedAsync(HttpMethod.Get, "/api/demo/customers", token);
        beforeUpdate.StatusCode.Should().Be(HttpStatusCode.OK);

        using var removeResponse = await host.SendAuthorizedAsync(
            HttpMethod.Delete,
            $"/api/admin/roles/{roleId}/permissions/{viewPermissionId}",
            token);
        using var assignResponse = await host.SendAuthorizedAsync(
            HttpMethod.Put,
            $"/api/admin/roles/{roleId}/permissions/{createPermissionId}",
            token);
        removeResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        assignResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var viewDenied = await host.SendAuthorizedAsync(HttpMethod.Get, "/api/demo/customers", token);
        using var createAllowed = await host.SendAuthorizedAsync(HttpMethod.Post, "/api/demo/customers", token);
        viewDenied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        createAllowed.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ChangingUsersRole_ShouldRefreshPermissionsForCurrentJwt()
    {
        await using var host = await PermissionApiHost.StartAsync();
        var (userId, oldRoleId) = await host.SeedUserAsync(
            "rolechange",
            "User!Password123",
            true,
            "customer.view",
            "user.role.assign");
        var newRoleId = await AddRoleWithPermissionAsync(host, "NEW_ROLE", "customer.create");
        var token = await host.LoginAsync("rolechange", "User!Password123");

        using var assignRoleResponse = await host.SendAuthorizedAsync(
            HttpMethod.Put,
            $"/api/admin/users/{userId}/role",
            token,
            new { roleId = newRoleId });
        assignRoleResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        oldRoleId.Should().NotBe(newRoleId);
        using var oldPermission = await host.SendAuthorizedAsync(HttpMethod.Get, "/api/demo/customers", token);
        using var newPermission = await host.SendAuthorizedAsync(HttpMethod.Post, "/api/demo/customers", token);
        oldPermission.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        newPermission.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AuthenticatedUserWithoutAdminPermission_CannotRegisterOrResetPasswords()
    {
        await using var host = await PermissionApiHost.StartAsync();
        await host.SeedUserAsync("ordinary", "User!Password123");
        var target = await host.SeedUserAsync("target", "Target!Password123");
        var token = await host.LoginAsync("ordinary", "User!Password123");

        using var registerRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register")
        {
            Content = JsonContent.Create(new
            {
                username = "forbiddenuser",
                email = "forbidden@example.test",
                fullName = "Forbidden User",
                roleId = target.RoleId,
                password = "Another!Password123"
            })
        };
        registerRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var registerResponse = await host.Client.SendAsync(registerRequest);

        using var resetRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/reset-password")
        {
            Content = JsonContent.Create(new { userId = target.UserId, newPassword = "Changed!Password123" })
        };
        resetRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var resetResponse = await host.Client.SendAsync(resetRequest);

        registerResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        resetResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SpoofedUserIdClaimWithoutValidSignature_ShouldNotAuthenticate()
    {
        await using var host = await PermissionApiHost.StartAsync();
        var forgedPayload = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes($"{{\"sub\":\"{Guid.NewGuid()}\",\"role\":\"ADMIN\"}}"));
        var forgedToken = $"e30.{forgedPayload}.forged-signature";
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/demo/customers");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", forgedToken);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task MultiplePermissionsAndDuplicateCodesAcrossRoles_ShouldNotDuplicateEffectivePermission()
    {
        await using var host = await PermissionApiHost.StartAsync();
        var (_, firstRoleId) = await host.SeedUserAsync(
            "roleone",
            "User!Password123",
            true,
            "customer.view",
            "customer.create");
        var (_, secondRoleId) = await host.SeedUserAsync(
            "roletwo",
            "User!Password123",
            true,
            "customer.view");
        var viewers = host.Services.GetRequiredService<IPermissionCache>();
        viewers.Should().NotBeNull();
        firstRoleId.Should().NotBe(secondRoleId);

        var firstToken = await host.LoginAsync("roleone", "User!Password123");
        var secondToken = await host.LoginAsync("roletwo", "User!Password123");
        using var firstView = await host.SendAuthorizedAsync(HttpMethod.Get, "/api/demo/customers", firstToken);
        using var firstCreate = await host.SendAuthorizedAsync(HttpMethod.Post, "/api/demo/customers", firstToken);
        using var secondView = await host.SendAuthorizedAsync(HttpMethod.Get, "/api/demo/customers", secondToken);
        using var secondCreate = await host.SendAuthorizedAsync(HttpMethod.Post, "/api/demo/customers", secondToken);

        firstView.StatusCode.Should().Be(HttpStatusCode.OK);
        firstCreate.StatusCode.Should().Be(HttpStatusCode.OK);
        secondView.StatusCode.Should().Be(HttpStatusCode.OK);
        secondCreate.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public void DependencyInjection_ShouldUseSingletonCacheAndScopedPermissionServices()
    {
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Server=(local);Database=lifetime;Trusted_Connection=True",
                ["JwtSettings:SecretKey"] = PermissionApiHost.TestSecret,
                ["JwtSettings:Issuer"] = PermissionApiHost.TestIssuer,
                ["JwtSettings:Audience"] = PermissionApiHost.TestAudience,
                ["PermissionCache:ExpirationSeconds"] = "60"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplicationServices();
        services.AddInfrastructureServices(configuration);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true
        });
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        var firstCache = firstScope.ServiceProvider.GetRequiredService<IPermissionCache>();
        var secondCache = secondScope.ServiceProvider.GetRequiredService<IPermissionCache>();
        var firstPermissionService = firstScope.ServiceProvider.GetRequiredService<IPermissionService>();
        var secondPermissionService = secondScope.ServiceProvider.GetRequiredService<IPermissionService>();
        var firstContext = firstScope.ServiceProvider.GetRequiredService<DynamicPermissionDbContext>();
        var secondContext = secondScope.ServiceProvider.GetRequiredService<DynamicPermissionDbContext>();
        var firstHandler = firstScope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationHandler>();
        var secondHandler = secondScope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationHandler>();

        firstCache.Should().BeSameAs(secondCache);
        firstPermissionService.Should().NotBeSameAs(secondPermissionService);
        firstContext.Should().NotBeSameAs(secondContext);
        firstHandler.Should().NotBeSameAs(secondHandler);
    }

    [Fact]
    public async Task CacheExpiration_ShouldReloadPermissionSnapshotFromDatabase()
    {
        await using var host = await PermissionApiHost.StartAsync(cacheExpirationSeconds: 1);
        var (userId, roleId) = await host.SeedUserAsync(
            "expires",
            "User!Password123",
            true,
            "customer.view");
        var token = await host.LoginAsync("expires", "User!Password123");
        using var initial = await host.SendAuthorizedAsync(HttpMethod.Get, "/api/demo/customers", token);
        initial.StatusCode.Should().Be(HttpStatusCode.OK);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<DynamicPermissionDbContext>>();
            await using var bypassContext = new DynamicPermissionDbContext(options);
            var assignment = await bypassContext.RolePermissions.SingleAsync(
                item => item.RoleId == roleId && item.Permission.Code == "customer.view");
            bypassContext.RolePermissions.Remove(assignment);
            await bypassContext.SaveChangesAsync();
        }

        await Task.Delay(TimeSpan.FromMilliseconds(1150));
        using var afterExpiry = await host.SendAuthorizedAsync(HttpMethod.Get, "/api/demo/customers", token);
        afterExpiry.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static async Task<Guid> AddRoleAsync(PermissionApiHost host, string code)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DynamicPermissionDbContext>();
        var role = PermissionApiHost.CreateRole(code);
        db.Roles.Add(role);
        await db.SaveChangesAsync();
        return role.Id;
    }

    private static async Task<Guid> AddRoleWithPermissionAsync(
        PermissionApiHost host,
        string roleCode,
        string permissionCode)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DynamicPermissionDbContext>();
        var role = PermissionApiHost.CreateRole(roleCode);
        var permission = PermissionApiHost.CreatePermission(permissionCode);
        db.Roles.Add(role);
        db.Permissions.Add(permission);
        db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id });
        await db.SaveChangesAsync();
        return role.Id;
    }

}

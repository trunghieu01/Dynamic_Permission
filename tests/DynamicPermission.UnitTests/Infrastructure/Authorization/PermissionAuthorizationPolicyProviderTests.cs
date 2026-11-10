using DynamicPermission.Infrastructure.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.Options;

namespace DynamicPermission.UnitTests.Infrastructure.Authorization;

public class PermissionAuthorizationPolicyProviderTests
{
    [Fact]
    public async Task GetPolicyAsync_ShouldCreatePolicyWithPermissionRequirement_ForGivenPermissionCode()
    {
        var provider = new PermissionAuthorizationPolicyProvider(Options.Create(new AuthorizationOptions()));

        var policy = await provider.GetPolicyAsync(
            $"{PermissionAuthorizationPolicyProvider.PolicyPrefix}customer.view");

        policy.Should().NotBeNull();
        policy!.Requirements.OfType<PermissionRequirement>()
            .Should().ContainSingle(requirement => requirement.PermissionCode == "customer.view");
        policy.Requirements.OfType<DenyAnonymousAuthorizationRequirement>().Should().ContainSingle();
    }

    [Fact]
    public async Task GetPolicyAsync_ShouldUseDefaultProvider_ForNonPermissionPolicy()
    {
        var options = new AuthorizationOptions();
        options.AddPolicy("AdministratorsOnly", policy => policy.RequireRole("ADMIN"));
        var provider = new PermissionAuthorizationPolicyProvider(Options.Create(options));

        var policy = await provider.GetPolicyAsync("AdministratorsOnly");

        policy.Should().NotBeNull();
        policy!.Requirements.Should().ContainSingle(requirement => requirement is RolesAuthorizationRequirement);
    }
}

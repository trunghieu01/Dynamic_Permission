using System.Security.Claims;
using DynamicPermission.Application.Common.Interfaces;
using DynamicPermission.Infrastructure.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DynamicPermission.UnitTests.Infrastructure.Authorization;

public class PermissionAuthorizationHandlerTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task HandleRequirement_ShouldSucceed_WhenUserHasRequiredPermission()
    {
        var permissionService = new Mock<IPermissionService>();
        permissionService
            .Setup(service => service.GetUserEffectivePermissionsAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { "customer.view" });
        var handler = CreateHandler(permissionService.Object);
        var context = CreateContext(CreateAuthenticatedPrincipal(UserId), "customer.view");

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleRequirement_ShouldNotSucceed_WhenUserLacksRequiredPermission()
    {
        var permissionService = new Mock<IPermissionService>();
        permissionService
            .Setup(service => service.GetUserEffectivePermissionsAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());
        var handler = CreateHandler(permissionService.Object);
        var context = CreateContext(CreateAuthenticatedPrincipal(UserId), "customer.view");

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleRequirement_ShouldNotSucceed_WhenUserIsNotAuthenticated()
    {
        var permissionService = new Mock<IPermissionService>();
        var handler = CreateHandler(permissionService.Object);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", UserId.ToString()) }));
        var context = CreateContext(principal, "customer.view");

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
        permissionService.Verify(
            service => service.GetUserEffectivePermissionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleRequirement_ShouldNotSucceed_WhenUserIdClaimIsMissing()
    {
        var permissionService = new Mock<IPermissionService>();
        var handler = CreateHandler(permissionService.Object);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "Test"));
        var context = CreateContext(principal, "customer.view");

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
        permissionService.Verify(
            service => service.GetUserEffectivePermissionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleRequirement_ShouldFailClosed_WhenPermissionServiceThrows()
    {
        var permissionService = new Mock<IPermissionService>();
        permissionService
            .Setup(service => service.GetUserEffectivePermissionsAsync(UserId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database unavailable"));
        var handler = CreateHandler(permissionService.Object);
        var context = CreateContext(CreateAuthenticatedPrincipal(UserId), "customer.view");

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
        context.HasFailed.Should().BeTrue();
    }

    private static PermissionAuthorizationHandler CreateHandler(IPermissionService permissionService)
    {
        return new PermissionAuthorizationHandler(
            permissionService,
            new HttpContextAccessor(),
            NullLogger<PermissionAuthorizationHandler>.Instance);
    }

    private static AuthorizationHandlerContext CreateContext(ClaimsPrincipal principal, string permissionCode)
    {
        return new AuthorizationHandlerContext(
            new[] { new PermissionRequirement(permissionCode) },
            principal,
            resource: null);
    }

    private static ClaimsPrincipal CreateAuthenticatedPrincipal(Guid userId)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) },
            authenticationType: "Test"));
    }
}

using System.Security.Claims;
using DynamicPermission.Application.Common.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace DynamicPermission.Infrastructure.Authorization;

public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly IPermissionService _permissionService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<PermissionAuthorizationHandler> _logger;

    public PermissionAuthorizationHandler(
        IPermissionService permissionService,
        IHttpContextAccessor httpContextAccessor,
        ILogger<PermissionAuthorizationHandler> logger)
    {
        _permissionService = permissionService;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        var userIdValue = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? context.User.FindFirst("sub")?.Value;
        if (!Guid.TryParse(userIdValue, out var userId))
        {
            return;
        }

        try
        {
            var permissions = await _permissionService.GetUserEffectivePermissionsAsync(
                userId,
                _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None);

            if (permissions.Contains(requirement.PermissionCode))
            {
                context.Succeed(requirement);
            }
        }
        catch (OperationCanceledException) when (_httpContextAccessor.HttpContext?.RequestAborted.IsCancellationRequested == true)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Permission check failed for user {UserId} and permission {PermissionCode}. Access is denied.",
                userId,
                requirement.PermissionCode);
            context.Fail();
        }
    }
}

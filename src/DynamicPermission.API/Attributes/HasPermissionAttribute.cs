using DynamicPermission.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace DynamicPermission.API.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class HasPermissionAttribute : AuthorizeAttribute
{
    public HasPermissionAttribute(string permissionCode)
    {
        if (string.IsNullOrWhiteSpace(permissionCode))
        {
            throw new ArgumentException("A permission code is required.", nameof(permissionCode));
        }

        Policy = $"{PermissionAuthorizationPolicyProvider.PolicyPrefix}{permissionCode}";
    }
}

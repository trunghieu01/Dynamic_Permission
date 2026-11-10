namespace DynamicPermission.Application.Common.Interfaces;

public interface IPermissionService
{
    Task<HashSet<string>> GetUserEffectivePermissionsAsync(Guid userId, CancellationToken cancellationToken = default);
}

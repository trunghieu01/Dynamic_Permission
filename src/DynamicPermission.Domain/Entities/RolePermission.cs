using DynamicPermission.Domain.Common;

namespace DynamicPermission.Domain.Entities;

public class RolePermission : AuditableJoinEntity
{
    public Guid RoleId { get; set; }
    public Guid PermissionId { get; set; }

    // Navigation properties
    public virtual Role Role { get; set; } = null!;
    public virtual Permission Permission { get; set; } = null!;
}

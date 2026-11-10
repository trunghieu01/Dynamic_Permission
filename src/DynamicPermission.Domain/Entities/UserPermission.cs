namespace DynamicPermission.Domain.Entities;

public class UserPermission
{
    public Guid UserId { get; set; }
    public Guid PermissionId { get; set; }
    public bool IsGranted { get; set; } // true: Grant, false: Deny

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    // Navigation properties
    public virtual User User { get; set; } = null!;
    public virtual Permission Permission { get; set; } = null!;
}

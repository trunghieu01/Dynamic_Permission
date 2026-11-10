namespace DynamicPermission.Domain.Common;

public abstract class AuditableJoinEntity
{
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? CreatedBy { get; set; }
}

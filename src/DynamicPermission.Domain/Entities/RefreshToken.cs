using DynamicPermission.Domain.Common;

namespace DynamicPermission.Domain.Entities;

public class RefreshToken : AuditableEntity<Guid>
{
    public Guid UserId { get; set; }
    public string Token { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public bool IsRevoked { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }

    // Navigation
    public virtual User User { get; set; } = null!;
}

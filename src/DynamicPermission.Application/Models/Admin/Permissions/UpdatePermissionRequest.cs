using System.ComponentModel.DataAnnotations;

namespace DynamicPermission.Application.Models.Admin.Permissions;

public sealed record UpdatePermissionRequest
{
    [Required, StringLength(100, MinimumLength = 3)]
    [RegularExpression(@"^[a-zA-Z0-9_.-]+$")]
    public required string Code { get; init; }

    [Required, StringLength(100)]
    public required string Name { get; init; }

    [Required, StringLength(50)]
    [RegularExpression(@"^[a-zA-Z0-9_.-]+$")]
    public required string Module { get; init; }

    [Required, StringLength(50)]
    [RegularExpression(@"^[a-zA-Z0-9_.-]+$")]
    public required string Action { get; init; }

    [StringLength(255)]
    public string? Description { get; init; }
}

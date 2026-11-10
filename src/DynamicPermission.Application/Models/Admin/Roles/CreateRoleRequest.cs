using System.ComponentModel.DataAnnotations;

namespace DynamicPermission.Application.Models.Admin.Roles;

public sealed record CreateRoleRequest
{
    [Required, StringLength(50, MinimumLength = 2)]
    [RegularExpression(@"^[a-zA-Z0-9_.-]+$")]
    public required string Code { get; init; }

    [Required, StringLength(100)]
    public required string Name { get; init; }

    [StringLength(255)]
    public string? Description { get; init; }
}

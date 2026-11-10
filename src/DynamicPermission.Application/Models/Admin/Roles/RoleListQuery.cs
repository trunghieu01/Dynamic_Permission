using System.ComponentModel.DataAnnotations;

namespace DynamicPermission.Application.Models.Admin.Roles;

public sealed record RoleListQuery : PageQuery
{
    [StringLength(150)]
    public string? Search { get; init; }

    public bool? IsActive { get; init; }
}

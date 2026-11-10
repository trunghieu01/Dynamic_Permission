using System.ComponentModel.DataAnnotations;

namespace DynamicPermission.Application.Models.Admin.Permissions;

public sealed record PermissionListQuery : PageQuery
{
    [StringLength(150)]
    public string? Search { get; init; }

    [StringLength(50)]
    public string? Module { get; init; }

    [StringLength(50)]
    public string? Action { get; init; }

    public bool? IsActive { get; init; }
}

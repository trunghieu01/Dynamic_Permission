using System.ComponentModel.DataAnnotations;

namespace DynamicPermission.Application.Models.Admin.Permissions;

public sealed record UpdatePermissionStatusRequest
{
    [Required]
    public bool? IsActive { get; init; }
}

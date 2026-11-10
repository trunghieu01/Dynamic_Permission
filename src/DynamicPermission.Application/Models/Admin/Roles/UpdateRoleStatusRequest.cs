using System.ComponentModel.DataAnnotations;

namespace DynamicPermission.Application.Models.Admin.Roles;

public sealed record UpdateRoleStatusRequest
{
    [Required]
    public bool? IsActive { get; init; }
}

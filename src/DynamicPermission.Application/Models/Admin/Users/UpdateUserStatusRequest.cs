using System.ComponentModel.DataAnnotations;

namespace DynamicPermission.Application.Models.Admin.Users;

public sealed record UpdateUserStatusRequest
{
    [Required]
    public bool? IsActive { get; init; }
}

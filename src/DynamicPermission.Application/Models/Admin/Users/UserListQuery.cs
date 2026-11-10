using System.ComponentModel.DataAnnotations;

namespace DynamicPermission.Application.Models.Admin.Users;

public sealed record UserListQuery : PageQuery
{
    [StringLength(150)]
    public string? Search { get; init; }

    public bool? IsActive { get; init; }

    public Guid? RoleId { get; init; }
}

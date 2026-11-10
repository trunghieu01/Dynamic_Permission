using System.ComponentModel.DataAnnotations;
using DynamicPermission.Application.Models.Admin;

namespace DynamicPermission.Application.Models.Admin.Users;

public sealed record CreateUserRequest
{
    [Required, StringLength(50, MinimumLength = 3)]
    [RegularExpression(@"^[a-zA-Z0-9_\.]+$")]
    public required string Username { get; init; }

    [Required, EmailAddress, StringLength(150)]
    public required string Email { get; init; }

    [Required, StringLength(100)]
    public required string FullName { get; init; }

    [Required, MinLength(6), StringLength(100)]
    public required string Password { get; init; }

    [NotEmptyGuid]
    public required Guid RoleId { get; init; }
}

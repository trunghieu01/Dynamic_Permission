using System.ComponentModel.DataAnnotations;

namespace DynamicPermission.Application.Models.Admin.Users;

public sealed record UpdateUserRequest
{
    [Required, StringLength(50, MinimumLength = 3)]
    [RegularExpression(@"^[a-zA-Z0-9_\.]+$")]
    public required string Username { get; init; }

    [Required, EmailAddress, StringLength(150)]
    public required string Email { get; init; }

    [Required, StringLength(100)]
    public required string FullName { get; init; }
}

namespace DynamicPermission.Application.Models.Auth;

public class RegisterUserResponse
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public Guid RoleId { get; set; }
    public string RoleCode { get; set; } = string.Empty;
    public string InitialPassword { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

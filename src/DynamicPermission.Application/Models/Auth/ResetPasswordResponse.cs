namespace DynamicPermission.Application.Models.Auth;

public class ResetPasswordResponse
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
    public string Message { get; set; } = "Mật khẩu đã được reset thành công.";
}

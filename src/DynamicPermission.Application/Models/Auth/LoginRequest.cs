using System.ComponentModel.DataAnnotations;

namespace DynamicPermission.Application.Models.Auth;

public class LoginRequest
{
    [Required(ErrorMessage = "Tên đăng nhập hoặc Email không được để trống.")]
    public string UsernameOrEmail { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mật khẩu không được để trống.")]
    public string Password { get; set; } = string.Empty;
}

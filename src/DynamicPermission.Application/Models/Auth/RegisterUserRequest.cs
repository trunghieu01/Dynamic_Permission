using System.ComponentModel.DataAnnotations;

namespace DynamicPermission.Application.Models.Auth;

public class RegisterUserRequest
{
    [Required(ErrorMessage = "Tên đăng nhập không được để trống.")]
    [StringLength(50, MinimumLength = 3, ErrorMessage = "Tên đăng nhập phải từ 3 đến 50 ký tự.")]
    [RegularExpression(@"^[a-zA-Z0-9_\.]+$", ErrorMessage = "Tên đăng nhập chỉ chứa chữ cái, số, dấu gạch dưới và dấu chấm.")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email không được để trống.")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")]
    [StringLength(150, ErrorMessage = "Email tối đa 150 ký tự.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Họ và tên không được để trống.")]
    [StringLength(100, ErrorMessage = "Họ và tên tối đa 100 ký tự.")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vai trò (RoleId) không được để trống.")]
    public Guid RoleId { get; set; }

    [StringLength(100, MinimumLength = 6, ErrorMessage = "Mật khẩu nếu nhập phải từ 6 ký tự trở lên.")]
    public string? Password { get; set; }
}

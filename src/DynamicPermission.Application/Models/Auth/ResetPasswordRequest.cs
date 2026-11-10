using System.ComponentModel.DataAnnotations;

namespace DynamicPermission.Application.Models.Auth;

public class ResetPasswordRequest
{
    [Required(ErrorMessage = "UserId không được để trống.")]
    public Guid UserId { get; set; }

    [StringLength(100, MinimumLength = 6, ErrorMessage = "Mật khẩu mới nếu nhập phải từ 6 ký tự trở lên.")]
    public string? NewPassword { get; set; }
}

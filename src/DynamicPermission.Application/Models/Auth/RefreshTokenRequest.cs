using System.ComponentModel.DataAnnotations;

namespace DynamicPermission.Application.Models.Auth;

public class RefreshTokenRequest
{
    [Required(ErrorMessage = "AccessToken không được để trống.")]
    public string AccessToken { get; set; } = string.Empty;

    [Required(ErrorMessage = "RefreshToken không được để trống.")]
    public string RefreshToken { get; set; } = string.Empty;
}

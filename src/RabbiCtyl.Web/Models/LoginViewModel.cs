using System.ComponentModel.DataAnnotations;

namespace RabbiCtyl.Web.Models;

public sealed class LoginViewModel
{
    [Required(ErrorMessage = "Enter your email address or username.")]
    [Display(Name = "Email or username")]
    public string Login { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter your password.")]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Remember me")]
    public bool RememberMe { get; set; }

    /// <summary>Challenge token filled in by the Turnstile/hCaptcha widget.</summary>
    public string? CaptchaToken { get; set; }
}

using System.ComponentModel.DataAnnotations;

namespace RabbiCtyl.Web.Models;

public sealed class ForgotPasswordViewModel
{
    [Required(ErrorMessage = "Enter the email address of your account.")]
    [EmailAddress]
    [StringLength(255)]
    public string Email { get; set; } = string.Empty;
}

public sealed class ResetPasswordViewModel
{
    [Required]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Token { get; set; } = string.Empty;

    [Required, StringLength(128, MinimumLength = 8), DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required, DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
    public string PasswordConfirmation { get; set; } = string.Empty;
}

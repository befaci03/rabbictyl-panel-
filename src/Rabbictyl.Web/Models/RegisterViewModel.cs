using System.ComponentModel.DataAnnotations;

namespace Rabbictyl.Web.Models;

public sealed class RegisterViewModel
{
    [Required(ErrorMessage = "Enter a username.")]
    [StringLength(32, MinimumLength = 3)]
    [RegularExpression("^[a-zA-Z0-9_.-]+$", ErrorMessage = "Use letters, digits, dots, dashes or underscores.")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter your email address.")]
    [EmailAddress]
    [StringLength(255)]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter your first name.")]
    [StringLength(64)]
    public string FirstName { get; set; } = string.Empty;

    [StringLength(64)]
    public string? LastName { get; set; }

    [Required(ErrorMessage = "Choose a password.")]
    [StringLength(128, MinimumLength = 8)]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Repeat your password.")]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
    public string PasswordConfirmation { get; set; } = string.Empty;

    public string? CaptchaToken { get; set; }
}

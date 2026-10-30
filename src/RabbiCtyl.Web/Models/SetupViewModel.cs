using System.ComponentModel.DataAnnotations;

namespace RabbiCtyl.Web.Models;

public sealed class SetupViewModel
{
    [Required(ErrorMessage = "Enter your first name.")]
    [StringLength(100, ErrorMessage = "First name must be 100 characters or fewer.")]
    [Display(Name = "First name")]
    public string FirstName { get; set; } = string.Empty;

    [StringLength(100, ErrorMessage = "Last name must be 100 characters or fewer.")]
    [Display(Name = "Last name (optional)")]
    public string? LastName { get; set; }

    [Required(ErrorMessage = "Choose a username.")]
    [StringLength(32, MinimumLength = 3, ErrorMessage = "Username must be 3–32 characters.")]
    [RegularExpression(
        "^[a-zA-Z0-9._-]+$",
        ErrorMessage = "Use letters, numbers, dots, underscores or dashes only.")]
    [Display(Name = "Username")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter your email address.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [StringLength(191, ErrorMessage = "Email must be 191 characters or fewer.")]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Choose a password.")]
    [StringLength(128, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters.")]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Confirm your password.")]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
    [Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

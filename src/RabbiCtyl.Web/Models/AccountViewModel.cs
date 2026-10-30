using System.ComponentModel.DataAnnotations;
using RabbiCtyl.Core.Entities;

namespace RabbiCtyl.Web.Models;

public sealed class AccountViewModel
{
    public User User { get; init; } = new();

    public IReadOnlyList<string> SshKeys { get; init; } = [];
}

public sealed class ChangePasswordForm
{
    [Required, DataType(DataType.Password)]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required, StringLength(128, MinimumLength = 8), DataType(DataType.Password)]
    public string NewPassword { get; set; } = string.Empty;

    [Required, DataType(DataType.Password)]
    [Compare(nameof(NewPassword), ErrorMessage = "The passwords do not match.")]
    public string NewPasswordConfirmation { get; set; } = string.Empty;
}

public sealed class AddSshKeyForm
{
    [Required, StringLength(4096, MinimumLength = 16)]
    public string Key { get; set; } = string.Empty;
}

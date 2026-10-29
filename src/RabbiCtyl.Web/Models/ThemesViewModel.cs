using RabbiCtyl.Core.Extensions;

namespace RabbiCtyl.Web.Models;

public sealed class ThemesViewModel
{
    public IReadOnlyList<ThemeDescriptor> Themes { get; init; } = [];

    public string ActiveThemeId { get; init; } = string.Empty;
}

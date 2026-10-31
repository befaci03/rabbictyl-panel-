using Rabbictyl.Core.Extensions;

namespace Rabbictyl.Web.Models;

public sealed class ThemesViewModel
{
    public IReadOnlyList<ThemeDescriptor> Themes { get; init; } = [];

    public string ActiveThemeId { get; init; } = string.Empty;
}

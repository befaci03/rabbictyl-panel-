namespace RabbiCtyl.Web.Models;

public sealed class AdminAddonPageViewModel
{
    public string Slug { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    /// <summary>Trusted HTML rendered by the addon itself.</summary>
    public string Html { get; init; } = string.Empty;
}

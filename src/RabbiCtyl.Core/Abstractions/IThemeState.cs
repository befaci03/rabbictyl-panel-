namespace RabbiCtyl.Core.Abstractions;

public interface IThemeState
{
    string ActiveThemeId { get; }

    Task ActivateAsync(string themeId, CancellationToken cancellationToken = default);
}

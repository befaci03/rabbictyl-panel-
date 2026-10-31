using System.Text.Json;
using Rabbictyl.Core.Entities;

namespace Rabbictyl.Core.Extensions;

/// <summary>
/// Authorized SSH keys used by the SFTP server. pats sends the authorized key
/// line as the credential when a client authenticates with a public key.
/// </summary>
public static class SshKeys
{
    public static IReadOnlyList<string> Parse(User user)
    {
        if (string.IsNullOrWhiteSpace(user.SshKeysJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(user.SshKeysJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static string Add(User user, string key)
    {
        var normalized = Normalize(key);

        if (normalized.Length == 0)
        {
            throw new ArgumentException("The key is empty.", nameof(key));
        }

        var keys = Parse(user).ToList();

        if (!keys.Contains(normalized, StringComparer.Ordinal))
        {
            keys.Add(normalized);
        }

        return JsonSerializer.Serialize(keys);
    }

    public static string Remove(User user, string key)
    {
        var normalized = Normalize(key);

        return JsonSerializer.Serialize(
            Parse(user).Where(existing => !existing.Equals(normalized, StringComparison.Ordinal)));
    }

    /// <summary>True when the presented key matches one of the stored keys.</summary>
    public static bool Matches(User user, string presented)
    {
        var normalized = Normalize(presented);

        return normalized.Length > 0
            && Parse(user).Any(existing => Normalize(existing).Equals(normalized, StringComparison.Ordinal));
    }

    /// <summary>
    /// Pterodactyl keys are <c>type base64 [comment]</c>; the comment is not part
    /// of the key material, so it is dropped for comparisons and storage.
    /// </summary>
    public static string Normalize(string key)
    {
        var parts = (key ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return parts.Length >= 2 ? $"{parts[0]} {parts[1]}" : string.Empty;
    }
}

using System.Text.RegularExpressions;

namespace Rabbictyl.Infrastructure.Extensions;

internal static partial class ExtensionPath
{
    [GeneratedRegex("^[a-zA-Z0-9][a-zA-Z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();

    public static bool IsValidId(string id) =>
        !string.IsNullOrWhiteSpace(id) && IdPattern().IsMatch(id);

    public static bool IsSafeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
        {
            return false;
        }

        var normalized = path.Replace('\\', '/');

        return normalized
            .Split('/', StringSplitOptions.None)
            .All(segment => segment is not ("" or "." or ".."));
    }

    public static string? ResolveWithin(string root, string relativePath)
    {
        if (!IsSafeRelativePath(relativePath))
        {
            return null;
        }

        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        var rootPrefix = fullRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;

        return fullPath.StartsWith(rootPrefix, StringComparison.Ordinal) ? fullPath : null;
    }

    public static string ContentTypeFor(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".css" => "text/css; charset=utf-8",
            ".js" => "text/javascript; charset=utf-8",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".avif" => "image/avif",
            ".ico" => "image/x-icon",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".ttf" => "font/ttf",
            ".otf" => "font/otf",
            ".json" => "application/json; charset=utf-8",
            ".txt" => "text/plain; charset=utf-8",
            ".md" => "text/markdown; charset=utf-8",
            _ => "application/octet-stream",
        };
}

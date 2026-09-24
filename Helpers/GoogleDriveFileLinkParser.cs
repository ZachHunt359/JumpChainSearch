namespace JumpChainSearch.Helpers;

public sealed record GoogleDriveFileLink(string FileId, string? ResourceKey);

public static class GoogleDriveFileLinkParser
{
    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "drive.google.com",
        "docs.google.com"
    };

    private static readonly HashSet<string> FileTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "document",
        "file",
        "presentation",
        "spreadsheets"
    };

    public static bool TryParse(string? value, out GoogleDriveFileLink? link)
    {
        link = null;

        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !AllowedHosts.Contains(uri.Host))
        {
            return false;
        }

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Any(segment => segment.Equals("folders", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        string? fileId = null;
        if (segments.Length >= 3
            && FileTypes.Contains(segments[0])
            && segments[1].Equals("d", StringComparison.OrdinalIgnoreCase))
        {
            fileId = segments[2];
        }
        else
        {
            fileId = GetQueryValue(uri.Query, "id");
        }

        if (!IsValidIdentifier(fileId))
        {
            return false;
        }

        var resourceKey = GetQueryValue(uri.Query, "resourcekey");
        if (resourceKey != null && !IsValidIdentifier(resourceKey))
        {
            return false;
        }

        link = new GoogleDriveFileLink(fileId!, resourceKey);
        return true;
    }

    private static bool IsValidIdentifier(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.Length <= 256
            && value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_');
    }

    private static string? GetQueryValue(string query, string name)
    {
        foreach (var item in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = item.Split('=', 2);
            if (parts.Length == 2
                && Uri.UnescapeDataString(parts[0]).Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }

        return null;
    }
}
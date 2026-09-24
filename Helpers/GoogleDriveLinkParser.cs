using Microsoft.AspNetCore.WebUtilities;
using System.Text.RegularExpressions;

namespace JumpChainSearch.Helpers;

public static partial class GoogleDriveLinkParser
{
    public static bool TryParseFolderUrl(string? value, out string driveId, out string? resourceKey)
    {
        driveId = string.Empty;
        resourceKey = null;

        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals("drive.google.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var foldersIndex = Array.FindIndex(segments, segment =>
            segment.Equals("folders", StringComparison.OrdinalIgnoreCase));

        if (foldersIndex >= 0 && foldersIndex + 1 < segments.Length)
        {
            driveId = segments[foldersIndex + 1];
        }
        else
        {
            var query = QueryHelpers.ParseQuery(uri.Query);
            driveId = query.TryGetValue("id", out var id) ? id.ToString() : string.Empty;
        }

        if (!DriveIdPattern().IsMatch(driveId))
        {
            driveId = string.Empty;
            return false;
        }

        var queryValues = QueryHelpers.ParseQuery(uri.Query);
        if (queryValues.TryGetValue("resourcekey", out var key) ||
            queryValues.TryGetValue("resourceKey", out key))
        {
            var parsedKey = key.ToString().Trim();
            resourceKey = parsedKey.Length > 0 ? parsedKey : null;
        }

        return true;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{10,200}$", RegexOptions.CultureInvariant)]
    private static partial Regex DriveIdPattern();
}
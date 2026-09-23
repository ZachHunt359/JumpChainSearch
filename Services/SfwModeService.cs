namespace JumpChainSearch.Services;

/// <summary>
/// Service to determine if the application is running in SFW (Safe For Work) mode.
/// SFW mode hides all NSFW-related content completely.
/// </summary>
public class SfwModeService
{
    private bool _isSfwMode;
    private readonly HashSet<string> _nsfwTags;
    private readonly HashSet<string> _nsfwChildTags;

    public SfwModeService(bool isSfwMode, HashSet<string>? nsfwTags = null, HashSet<string>? nsfwChildTags = null)
    {
        _isSfwMode = isSfwMode;
        _nsfwTags = nsfwTags ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "NSFW",
            "NSFW-ish",
            "Lewd",
            "Harem",
            "Porn Parody",
            "Incest",
            "Rape"
        };
        _nsfwChildTags = nsfwChildTags ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns true if the application is running in SFW mode.
    /// </summary>
    public bool IsSfwMode => _isSfwMode;

    public IReadOnlySet<string> KnownNsfwTags => _nsfwTags;

    public static bool IsSfwHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return false;

        return host.Equals("jumpchainsearch.net", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".jumpchainsearch.net", StringComparison.OrdinalIgnoreCase) ||
               host.Equals("jumpchainsearch.org", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".jumpchainsearch.org", StringComparison.OrdinalIgnoreCase);
    }

    public void SetSfwMode(bool isSfwMode)
    {
        _isSfwMode = isSfwMode;
    }

    /// <summary>
    /// Checks if a tag name is NSFW or a child of an NSFW tag.
    /// </summary>
    public bool IsNsfwTag(string tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName))
            return false;

        if (tagName.Contains("NSFW", StringComparison.OrdinalIgnoreCase))
            return true;

        // Check against known NSFW tags
        if (_nsfwTags.Contains(tagName))
            return true;

        // Check against known child tags of NSFW
        if (_nsfwChildTags.Contains(tagName))
            return true;

        return false;
    }

    /// <summary>
    /// Updates the list of NSFW tags from the tag hierarchy.
    /// </summary>
    public void UpdateNsfwTags(HashSet<string> nsfwTags)
    {
        _nsfwTags.Clear();
        foreach (var tag in nsfwTags)
        {
            _nsfwTags.Add(tag);
        }
    }

    /// <summary>
    /// Updates the list of child tags that belong to NSFW parent tags.
    /// </summary>
    public void UpdateNsfwChildTags(HashSet<string> childTags)
    {
        _nsfwChildTags.Clear();
        foreach (var tag in childTags)
        {
            _nsfwChildTags.Add(tag);
        }
    }
}

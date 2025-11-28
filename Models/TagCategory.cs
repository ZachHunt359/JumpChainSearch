namespace JumpChainSearch.Models;

/// <summary>
/// Standard tag categories used throughout the application.
/// This is the single source of truth for tag category values.
/// Based on actual database usage as of November 2025.
/// </summary>
public static class TagCategory
{
    // Core content categories
    public const string Genre = "Genre";
    public const string Series = "Series";
    public const string Content = "Content";
    public const string ContentType = "ContentType";
    
    // Technical/metadata categories
    public const string Format = "Format";
    public const string Size = "Size";
    public const string Version = "Version";
    public const string Status = "Status";
    public const string Extraction = "Extraction";
    public const string Drive = "Drive";
    
    /// <summary>
    /// All valid tag categories in display order
    /// </summary>
    public static readonly string[] All = new[]
    {
        Genre,
        Series,
        Content,
        ContentType,
        Format,
        Size,
        Version,
        Status,
        Extraction,
        Drive
    };
    
    /// <summary>
    /// User-facing categories (excludes technical Drive category)
    /// </summary>
    public static readonly string[] UserFacing = new[]
    {
        Genre,
        Series,
        Content,
        ContentType,
        Format,
        Size,
        Version,
        Status,
        Extraction
    };
}

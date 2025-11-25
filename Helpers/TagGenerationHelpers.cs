using JumpChainSearch.Models;

namespace JumpChainSearch.Helpers;

public static class TagGenerationHelpers
{
    public static string DetermineContentType(string fileName, string folderPath)
    {
        string combinedPath = $"{folderPath}/{fileName}".ToLowerInvariant();

        if (combinedPath.Contains("gauntlet")) return "Gauntlet";
        if (combinedPath.Contains("supplement")) return "Supplement";
        if (combinedPath.Contains("stories") || combinedPath.Contains("story") || combinedPath.Contains("fanfic")) return "Story";
        if (combinedPath.Contains("upload") || combinedPath.Contains("new ")) return "New Upload";
        
        return "JumpDoc";
    }

    public static string DetermineFileFormat(string mimeType, string fileName)
    {
        return mimeType switch
        {
            "application/pdf" => "PDF",
            "application/vnd.google-apps.document" => "Google Doc",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => "Word Doc",
            "text/plain" => "Text",
            _ when fileName.ToLowerInvariant().EndsWith(".rtf") => "RTF",
            _ when fileName.ToLowerInvariant().EndsWith(".odt") => "OpenDocument",
            _ => "Document"
        };
    }

    public static string DetermineSizeCategory(long sizeBytes)
    {
        return sizeBytes switch
        {
            < 1024 * 1024 => "Small",
            < 10 * 1024 * 1024 => "Medium",
            < 50 * 1024 * 1024 => "Large",
            _ => "Very Large"
        };
    }

    public static void AddQualityTags(List<DocumentTag> tags, string fileName, string folderPath, int documentId)
    {
        string fullPath = $"{folderPath}/{fileName}".ToLowerInvariant();

        if (fullPath.Contains("wip") || fullPath.Contains("work in progress") || fullPath.Contains("incomplete"))
            tags.Add(new DocumentTag { TagName = "Work in Progress", TagCategory = "Status", JumpDocumentId = documentId });
        
        if (fullPath.Contains("complete") || fullPath.Contains("finished"))
            tags.Add(new DocumentTag { TagName = "Complete", TagCategory = "Status", JumpDocumentId = documentId });
            
        if (fullPath.Contains("draft") || fullPath.Contains("rough"))
            tags.Add(new DocumentTag { TagName = "Draft", TagCategory = "Status", JumpDocumentId = documentId });

        if (fullPath.Contains("nsfw") || fullPath.Contains("adult"))
            tags.Add(new DocumentTag { TagName = "NSFW", TagCategory = "Content", JumpDocumentId = documentId });

        if (fullPath.Contains("v1.") || fullPath.Contains("version 1"))
            tags.Add(new DocumentTag { TagName = "v1.x", TagCategory = "Version", JumpDocumentId = documentId });
        if (fullPath.Contains("v2.") || fullPath.Contains("version 2"))
            tags.Add(new DocumentTag { TagName = "v2.x", TagCategory = "Version", JumpDocumentId = documentId });
    }

    public static void AddSeriesTags(List<DocumentTag> tags, string fileName, string folderPath, int documentId)
    {
        string fullPath = $"{folderPath}/{fileName}".ToLowerInvariant();

        // Load series mappings from JSON file (single source of truth)
        var franchises = LoadSeriesMappingsFromJson();

        foreach (var franchise in franchises)
        {
            // Use negative lookahead (?!anon) to prevent "skyrim" from matching "skyrimanon"
            if (franchise.Value.Any(keyword => System.Text.RegularExpressions.Regex.IsMatch(fullPath, $@"\b{System.Text.RegularExpressions.Regex.Escape(keyword)}(?!anon)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)))
            {
                tags.Add(new DocumentTag { TagName = franchise.Key, TagCategory = "Series", JumpDocumentId = documentId });
                //break; 
                // Can add more than one series tag
            }
        }
    }

    private static Dictionary<string, List<string>> LoadSeriesMappingsFromJson()
    {
        var jsonPath = Path.Combine(AppContext.BaseDirectory, "series-mappings.json");
        
        if (!File.Exists(jsonPath))
        {
            // Return empty dictionary if file doesn't exist
            Console.WriteLine($"Warning: series-mappings.json not found at {jsonPath}");
            return new Dictionary<string, List<string>>();
        }
        
        var json = File.ReadAllText(jsonPath);
        var doc = System.Text.Json.JsonDocument.Parse(json);
        
        var mappings = new Dictionary<string, List<string>>();
        
        if (doc.RootElement.TryGetProperty("seriesMappings", out var seriesMappingsElement))
        {
            foreach (var series in seriesMappingsElement.EnumerateObject())
            {
                var patterns = new List<string>();
                foreach (var pattern in series.Value.EnumerateArray())
                {
                    patterns.Add(pattern.GetString() ?? "");
                }
                mappings[series.Name] = patterns;
            }
        }
        
        return mappings;
    }

    public static void AddGenreTags(List<DocumentTag> tags, string fileName, string folderPath, int documentId)
    {
        string fullPath = $"{folderPath}/{fileName}".ToLowerInvariant();

        var genres = new Dictionary<string, string[]>
        {
            ["Slice of Life"] = new[] { "slice of life", "sol", "daily life", "mundane" },
            ["Historical/Alt-Historical/Lost World"] = new[] { "historical", "alt-history", "alternate history", "lost world", "time period", "ancient", "medieval" },
            ["Survival"] = new[] { "survival", "wilderness", "apocalypse", "post-apocalyptic", "zombie" },
            ["Modern Adventure"] = new[] { "modern adventure", "contemporary", "urban adventure" },
            ["Military"] = new[] { "military", "war", "soldier", "combat", "army", "navy", "marine" },
            ["Horror"] = new[] { "horror", "scary", "terror", "creepy", "nightmare" },
            ["Super Hero"] = new[] { "superhero", "super hero", "cape", "powered", "metahuman" },
            ["Science Fiction"] = new[] { "science fiction", "sci-fi", "scifi", "space", "future", "cyberpunk", "dystopia" },
            ["Modern Occult"] = new[] { "occult", "supernatural", "paranormal", "urban fantasy", "modern magic" },
            ["Fantasy"] = new[] { "fantasy", "magic", "medieval fantasy", "sword and sorcery", "high fantasy", "epic fantasy" }
        };

        foreach (var genre in genres)
        {
            if (genre.Value.Any(keyword => fullPath.Contains(keyword)))
            {
                tags.Add(new DocumentTag { TagName = genre.Key, TagCategory = "Genre", JumpDocumentId = documentId });
                //break; 
                // Can add more than one genre tag
            }
        }
    }

    /// <summary>
    /// Add genre tags by matching Google Drive File ID from scraped community genre mappings.
    /// This is more accurate than name-based matching as it prevents false positives.
    /// </summary>
    public static void AddGenreTagsByFileId(List<DocumentTag> tags, string googleDriveFileId, int documentId)
    {
        if (string.IsNullOrEmpty(googleDriveFileId))
            return;

        var genreMappings = LoadGenreMappingsFromJson();

        foreach (var genre in genreMappings)
        {
            if (genre.Value.Any(doc => doc.DriveFileId == googleDriveFileId))
            {
                tags.Add(new DocumentTag { TagName = genre.Key, TagCategory = "Genre", JumpDocumentId = documentId });
                // Can add more than one genre tag - don't break
            }
        }
    }

    private static Dictionary<string, List<GenreDocumentMapping>> LoadGenreMappingsFromJson()
    {
        var jsonPath = Path.Combine(Directory.GetCurrentDirectory(), "genre-mappings-scraped.json");
        
        if (!File.Exists(jsonPath))
        {
            Console.WriteLine($"Warning: genre-mappings-scraped.json not found at {jsonPath}");
            return new Dictionary<string, List<GenreDocumentMapping>>();
        }
        
        var json = File.ReadAllText(jsonPath);
        var doc = System.Text.Json.JsonDocument.Parse(json);
        
        var mappings = new Dictionary<string, List<GenreDocumentMapping>>();
        
        if (doc.RootElement.TryGetProperty("genreMappings", out var genreMappingsElement))
        {
            foreach (var genre in genreMappingsElement.EnumerateObject())
            {
                var documents = new List<GenreDocumentMapping>();
                
                // Handle both old format (string array) and new format (object array with driveFileId)
                foreach (var item in genre.Value.EnumerateArray())
                {
                    if (item.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        // Old format: just document name (skip these, we need file IDs)
                        continue;
                    }
                    else if (item.ValueKind == System.Text.Json.JsonValueKind.Object)
                    {
                        // New format: object with name and driveFileId
                        string name = item.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? "" : "";
                        string fileId = item.TryGetProperty("driveFileId", out var fileIdElement) ? fileIdElement.GetString() ?? "" : "";
                        
                        if (!string.IsNullOrEmpty(fileId))
                        {
                            documents.Add(new GenreDocumentMapping { Name = name, DriveFileId = fileId });
                        }
                    }
                }
                
                if (documents.Any())
                {
                    mappings[genre.Name] = documents;
                }
            }
        }
        
        return mappings;
    }

    public class GenreDocumentMapping
    {
        public string Name { get; set; } = "";
        public string DriveFileId { get; set; } = "";
    }

    public static void AddTextExtractionTag(List<DocumentTag> tags, string? extractedText, int documentId)
    {
        // Only add "Has Text" tag if there is actually extracted text
        if (!string.IsNullOrEmpty(extractedText))
        {
            tags.Add(new DocumentTag { TagName = "Has Text", TagCategory = "Extraction", JumpDocumentId = documentId });
        }
    }

    // Helper methods for backward compatibility
    public static string DetermineContentTypeHelper(string fileName, string folderPath)
    {
        var name = fileName?.ToLower() ?? "";
        var folder = folderPath?.ToLower() ?? "";
        
        if (name.Contains("gauntlet") || folder.Contains("gauntlet"))
            return "Gauntlet";
        if (name.Contains("supplement") || folder.Contains("supplement"))
            return "Supplement";
        if (name.Contains("story") || folder.Contains("story") || folder.Contains("stories"))
            return "Story";
        
        return "JumpDoc";
    }

    public static string DetermineFileFormatHelper(string mimeType, string fileName)
    {
        if (mimeType?.Contains("pdf") == true || fileName?.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) == true)
            return "PDF";
        if (mimeType?.Contains("document") == true || fileName?.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) == true || 
            fileName?.EndsWith(".doc", StringComparison.OrdinalIgnoreCase) == true)
            return "Document";
        if (mimeType?.Contains("text") == true || fileName?.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) == true)
            return "Text";
        if (mimeType?.Contains("image") == true || fileName?.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) == true ||
            fileName?.EndsWith(".png", StringComparison.OrdinalIgnoreCase) == true)
            return "Image";
        
        return "Other";
    }

    public static string DetermineSizeCategoryHelper(long size)
    {
        if (size < 100 * 1024) // < 100KB
            return "Small";
        if (size < 1024 * 1024) // < 1MB
            return "Medium";
        if (size < 10 * 1024 * 1024) // < 10MB
            return "Large";
        
        return "XLarge";
    }
}
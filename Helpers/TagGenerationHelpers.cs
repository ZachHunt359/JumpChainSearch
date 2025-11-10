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

        var franchises = new Dictionary<string, string[]>
        {
            ["Marvel"] = new[] { "marvel", "x-men", "avengers", "spider-man", "iron man", "captain america" },
            ["DC Comics"] = new[] { "batman", "superman", "wonder woman", "justice league", "flash", "green lantern" },
            ["Star Wars"] = new[] { "star wars", "jedi", "sith", "clone wars" },
            ["Star Trek"] = new[] { "star trek", "enterprise", "voyager", "ds9", "picard" },
            ["Pokemon"] = new[] { "pokemon", "pokémon" },
            ["Naruto"] = new[] { "naruto", "konoha" },
            ["My Hero Academia"] = new[] { "my hero academia", "my hero academy", "boku no hero", "plus ultra" },
            ["Dragon Ball"] = new[] { "dragon ball", "goku", "vegeta", "frieza" },
            ["Harry Potter"] = new[] { "harry potter", "hogwarts", "wizarding world" },
            ["Warhammer"] = new[] { "warhammer", "40k", "space marine" },
            ["Generic"] = new[] { "generic" },
            ["Out of Context"] = new[] { "out of context", "ooc" },
            ["Isekai"] = new[] { "isekai"},
            ["Jurassic World"] = new[] { "jurassic park", "jurassic world","dinosaurs", "isla nublar" },
            ["Men in Black"] = new[] { "men in black", "mib" },
            ["Nasuverse"] = new[] { "fate/stay night", "fate zero", "nasuverse", "type-moon", "type moon", "fate/"}
        };

        foreach (var franchise in franchises)
        {
            if (franchise.Value.Any(keyword => fullPath.Contains(keyword)))
            {
                tags.Add(new DocumentTag { TagName = franchise.Key, TagCategory = "Series", JumpDocumentId = documentId });
                //break; 
                // Can add more than one series tag
            }
        }
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
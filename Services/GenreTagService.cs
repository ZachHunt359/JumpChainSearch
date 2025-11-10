using JumpChainSearch.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace JumpChainSearch.Services;

public class GenreTagService
{
    private readonly JumpChainDbContext _context;
    
    public GenreTagService(JumpChainDbContext context)
    {
        _context = context;
    }
    
    public async Task<(int matched, int tagged)> ApplyGenreTagsFromCommunityList()
    {
        var genreMappings = LoadGenreMappingsFromJson();
        int matchedCount = 0;
        int taggedCount = 0;
        
        foreach (var (genre, documentPatterns) in genreMappings)
        {
            Console.WriteLine($"Processing genre: {genre} ({documentPatterns.Count} patterns)");
            
            foreach (var pattern in documentPatterns)
            {
                // Search for documents matching this pattern
                var documents = await _context.JumpDocuments
                    .Include(d => d.Tags)
                    .Where(d => EF.Functions.Like(d.Name, $"%{pattern}%") || 
                               EF.Functions.Like(d.FolderPath, $"%{pattern}%"))
                    .ToListAsync();
                    
                foreach (var doc in documents)
                {
                    matchedCount++;
                    
                    // Check if Genre tag already exists
                    var existingTag = doc.Tags.FirstOrDefault(t => t.TagName == genre && t.TagCategory == "Genre");
                    if (existingTag == null)
                    {
                        doc.Tags.Add(new Models.DocumentTag
                        {
                            TagName = genre,
                            TagCategory = "Genre"
                        });
                        taggedCount++;
                    }
                }
            }
            
            Console.WriteLine($"  Matched {matchedCount} documents");
        }
        
        await _context.SaveChangesAsync();
        Console.WriteLine($"Applied {taggedCount} new genre tags across {matchedCount} documents");
        return (matchedCount, taggedCount);
    }
    
    private Dictionary<string, List<string>> LoadGenreMappingsFromJson()
    {
        var jsonPath = Path.Combine(Directory.GetCurrentDirectory(), "genre-mappings.json");
        
        if (!File.Exists(jsonPath))
        {
            throw new FileNotFoundException($"Genre mappings file not found: {jsonPath}");
        }
        
        var json = File.ReadAllText(jsonPath);
        var doc = JsonDocument.Parse(json);
        
        var mappings = new Dictionary<string, List<string>>();
        
        if (doc.RootElement.TryGetProperty("genreMappings", out var genreMappingsElement))
        {
            foreach (var genre in genreMappingsElement.EnumerateObject())
            {
                var patterns = new List<string>();
                foreach (var pattern in genre.Value.EnumerateArray())
                {
                    patterns.Add(pattern.GetString() ?? "");
                }
                mappings[genre.Name] = patterns;
            }
        }
        
        return mappings;
    }
}

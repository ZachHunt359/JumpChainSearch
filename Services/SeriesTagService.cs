using JumpChainSearch.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace JumpChainSearch.Services;

public class SeriesTagService
{
    private readonly JumpChainDbContext _context;
    private readonly TagRuleService _tagRuleService;
    
    public SeriesTagService(JumpChainDbContext context, TagRuleService tagRuleService)
    {
        _context = context;
        _tagRuleService = tagRuleService;
    }
    
    public async Task<(int matched, int tagged)> ApplySeriesTagsFromCommunityList()
    {
        // First, remove ALL existing Series tags to ensure clean reapplication
        Console.WriteLine("Removing all existing Series tags...");
        var existingSeriesTags = await _context.DocumentTags
            .Where(t => t.TagCategory == "Series")
            .ToListAsync();
        _context.DocumentTags.RemoveRange(existingSeriesTags);
        await _context.SaveChangesAsync();
        Console.WriteLine($"Removed {existingSeriesTags.Count} existing Series tags");
        
        var seriesMappings = LoadSeriesMappingsFromJson();
        int matchedCount = 0;
        int taggedCount = 0;
        
        foreach (var (series, documentPatterns) in seriesMappings)
        {
            Console.WriteLine($"Processing series: {series} ({documentPatterns.Count} patterns)");
            
            foreach (var pattern in documentPatterns)
            {
                // Use SQL LIKE for fast pre-filtering, then apply regex in C#
                var likePattern = $"%{pattern}%";
                var documents = await _context.JumpDocuments
                    .Include(d => d.Tags)
                    .Where(d => EF.Functions.Like(d.Name, likePattern) || EF.Functions.Like(d.FolderPath, likePattern))
                    .ToListAsync();
                
                // Build regex pattern with word boundaries and negative lookahead to exclude "anon" suffix
                // This prevents "skyrim" from matching "skyrimanon"
                var wordBoundaryPattern = $@"\b{System.Text.RegularExpressions.Regex.Escape(pattern)}(?!anon)\b";
                
                // Filter in-memory with enhanced regex to avoid false matches
                var filteredDocs = documents.Where(d => 
                    System.Text.RegularExpressions.Regex.IsMatch(d.Name ?? "", wordBoundaryPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase) ||
                    System.Text.RegularExpressions.Regex.IsMatch(d.FolderPath ?? "", wordBoundaryPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                ).ToList();
                    
                foreach (var doc in filteredDocs)
                {
                    matchedCount++;
                    
                    // Check if Series tag already exists
                    var existingTag = doc.Tags.FirstOrDefault(t => t.TagName == series && t.TagCategory == "Series");
                    if (existingTag == null)
                    {
                        doc.Tags.Add(new Models.DocumentTag
                        {
                            TagName = series,
                            TagCategory = "Series"
                        });
                        taggedCount++;
                    }
                }
            }
            
            Console.WriteLine($"  Matched {matchedCount} documents");
        }
        
        await _context.SaveChangesAsync();
        Console.WriteLine($"Applied {taggedCount} new series tags across {matchedCount} documents");
        
        // Apply approved rules to restore community-voted Series tags after regeneration
        Console.WriteLine("Applying approved Series tag rules...");
        var rulesResult = await _tagRuleService.ApplyApprovedRules("Series");
        Console.WriteLine($"Applied {rulesResult.AdditionsApplied} tag additions and {rulesResult.RemovalsApplied} tag removals from {rulesResult.TotalRules} Series rules");
        
        return (matchedCount, taggedCount);
    }
    
    private Dictionary<string, List<string>> LoadSeriesMappingsFromJson()
    {
        var jsonPath = Path.Combine(AppContext.BaseDirectory, "series-mappings.json");
        
        if (!File.Exists(jsonPath))
        {
            throw new FileNotFoundException($"Series mappings file not found: {jsonPath}");
        }
        
        var json = File.ReadAllText(jsonPath);
        var doc = JsonDocument.Parse(json);
        
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
}

using JumpChainSearch.Data;
using JumpChainSearch.Models;
using JumpChainSearch.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using System.Text;

namespace JumpChainSearch.Extensions;

/// <summary>
/// Optimized search endpoints with:
/// - FTS5 full-text search with BM25 ranking
/// - AND logic for search terms (find documents with ALL terms)
/// - Response caching with configurable TTL (default 5 minutes)
/// - Minus operator support (drag -dragon excludes "dragon")
/// - Quoted phrase support ("dragon age" as exact match)
/// - Tag filtering with AND/NOT logic
/// </summary>
public static class SearchEndpointsOptimized
{
    private static TimeSpan _cacheDuration = TimeSpan.FromMinutes(5);
    
    public static void SetCacheDuration(int minutes)
    {
        _cacheDuration = TimeSpan.FromMinutes(minutes);
    }
    
    public static RouteGroupBuilder MapOptimizedSearchEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", OptimizedSearch);
        group.MapGet("/random", GetRandomDocuments);
        group.MapGet("/tags", GetTagFrequencies);
        group.MapGet("/tags/batch", GetDocumentTagsBatch);
        group.MapGet("/count", GetDocumentCount);
        return group;
    }

    private static async Task<IResult> OptimizedSearch(
        JumpChainDbContext context,
        IMemoryCache cache,
        Fts5SearchService fts5Service,
        SfwModeService? sfwMode = null,
        string? q = null,
        int limit = 50,
        int offset = 0,
        string? includeTags = null,
        string? excludeTags = null,
        int? docId = null)
    {
        try
        {
            // In SFW mode, automatically exclude NSFW tags
            if (sfwMode?.IsSfwMode == true)
            {
                var nsfwExclusion = "NSFW";
                excludeTags = string.IsNullOrWhiteSpace(excludeTags) 
                    ? nsfwExclusion 
                    : $"{excludeTags},{nsfwExclusion}";
            }
            
            // Skip caching if docId is provided (direct document lookup)
            if (docId.HasValue)
            {
                // Direct document lookup - don't use cache
                var query = context.JumpDocuments
                    .AsNoTracking()
                    .Include(d => d.Tags)
                    .Where(d => d.Id == docId.Value);
                
                var document = await query.FirstOrDefaultAsync();
                
                if (document == null)
                {
                    return Results.Ok(new
                    {
                        success = true,
                        query = q ?? "",
                        includeTags = includeTags ?? "",
                        excludeTags = excludeTags ?? "",
                        resultCount = 0,
                        totalCount = 0,
                        results = new List<object>()
                    });
                }
                
                var result = new
                {
                    document.Id,
                    document.Name,
                    document.FolderPath,
                    document.SourceDrive,
                    document.MimeType,
                    document.Size,
                    document.GoogleDriveFileId,
                    document.WebViewLink,
                    document.DownloadLink,
                    HasExtractedText = !string.IsNullOrEmpty(document.ExtractedText),
                    ExtractedTextLength = document.ExtractedText != null ? document.ExtractedText.Length : 0,
                    Tags = document.Tags.Select(t => t.TagName).ToList(),
                    document.CreatedTime,
                    document.ModifiedTime,
                    document.LastModified,
                    Score = 0
                };
                
                return Results.Ok(new
                {
                    success = true,
                    query = q ?? "",
                    includeTags = includeTags ?? "",
                    excludeTags = excludeTags ?? "",
                    resultCount = 1,
                    totalCount = 1,
                    results = new[] { result }
                });
            }
            
            // Generate cache key
            var cacheKey = GenerateCacheKey(q, limit, offset, includeTags, excludeTags);
            
            // Try to get from cache
            if (cache.TryGetValue(cacheKey, out object? cachedResult) && cachedResult != null)
            {
                return Results.Ok(cachedResult);
            }

            // Parse search query into terms, excluded terms, and phrases
            var (searchTerms, excludedTerms, phrases) = ParseAdvancedSearchQuery(q);
            
            // If we have search criteria, use FTS5
            if (searchTerms.Any() || phrases.Any() || excludedTerms.Any())
            {
                // Build FTS5 query
                var fts5Query = fts5Service.BuildFts5Query(searchTerms, phrases, excludedTerms);
                
                // If we have tag filters, we need to apply them BEFORE pagination
                bool hasTagFilters = !string.IsNullOrWhiteSpace(includeTags) || !string.IsNullOrWhiteSpace(excludeTags);
                
                List<(int Id, double Score)> fts5Results;
                int totalCount;
                
                if (hasTagFilters)
                {
                    // Strategy: Get more results than we need, filter by tags, then paginate
                    // Use a reasonable multiplier to balance accuracy vs performance
                    var fetchLimit = Math.Min(limit * 20, 2000); // Fetch up to 2000 results max
                    var fetchOffset = offset; // Start at the requested offset
                    
                    Console.WriteLine($"[SEARCH] Tag filters detected, fetching {fetchLimit} FTS5 results starting at {fetchOffset}");
                    fts5Results = await fts5Service.SearchFts5Async(fts5Query, fetchLimit, fetchOffset);
                    
                    if (fts5Results.Count == 0)
                    {
                        var emptyResponse = new
                        {
                            success = true,
                            query = q ?? "",
                            includeTags = includeTags ?? "",
                            excludeTags = excludeTags ?? "",
                            resultCount = 0,
                            totalCount = 0,
                            results = new List<object>()
                        };
                        
                        cache.Set(cacheKey, emptyResponse, _cacheDuration);
                        return Results.Ok(emptyResponse);
                    }
                    
                    // Get all document IDs and apply tag filtering
                    var allDocumentIds = fts5Results.Select(r => r.Id).ToList();
                    Console.WriteLine($"[SEARCH] FTS5 returned {allDocumentIds.Count} documents, filtering by tags...");
                    
                    // Query for IDs only with tag filtering applied
                    var filteredQuery = context.JumpDocuments
                        .AsNoTracking()
                        .Where(d => allDocumentIds.Contains(d.Id));
                    
                    filteredQuery = ApplyTagFilters(filteredQuery, includeTags, excludeTags);
                    
                    // Get filtered document IDs
                    var filteredDocumentIds = await filteredQuery.Select(d => d.Id).ToListAsync();
                    Console.WriteLine($"[SEARCH] After tag filtering: {filteredDocumentIds.Count} documents remain");
                    
                    // Filter FTS5 results to only include documents that passed tag filtering
                    fts5Results = fts5Results.Where(r => filteredDocumentIds.Contains(r.Id)).ToList();
                    
                    // Calculate correct total and apply pagination
                    totalCount = fts5Results.Count;
                    fts5Results = fts5Results.Skip(0).Take(limit).ToList();
                    
                    Console.WriteLine($"[SEARCH] After pagination (limit={limit}): {fts5Results.Count} documents");
                }
                else
                {
                    // No tag filters - use original pagination logic
                    totalCount = await fts5Service.GetFts5CountAsync(fts5Query);
                    fts5Results = await fts5Service.SearchFts5Async(fts5Query, limit, offset);
                    
                    if (fts5Results.Count == 0)
                    {
                        var emptyResponse = new
                        {
                            success = true,
                            query = q ?? "",
                            includeTags = includeTags ?? "",
                            excludeTags = excludeTags ?? "",
                            resultCount = 0,
                            totalCount = 0,
                            results = new List<object>()
                        };
                        
                        cache.Set(cacheKey, emptyResponse, _cacheDuration);
                        return Results.Ok(emptyResponse);
                    }
                }
                
                // Get document IDs from FTS5 results
                Console.WriteLine($"[SEARCH] Processing {fts5Results.Count} FTS5 results");
                var documentIds = fts5Results.Select(r => r.Id).ToList();
                var scoreMap = fts5Results.ToDictionary(r => r.Id, r => r.Score);
                Console.WriteLine($"[SEARCH] Created scoreMap with {scoreMap.Count} entries");
                
                // Fetch full document details (tags already filtered if tag filters were applied)
                Console.WriteLine($"[SEARCH] Fetching documents for IDs: {string.Join(", ", documentIds.Take(5))}...");
                var query = context.JumpDocuments
                    .AsNoTracking()
                    .Include(d => d.Tags)
                    .Where(d => documentIds.Contains(d.Id));
                
                // Apply docId filter if provided
                if (docId.HasValue)
                {
                    query = query.Where(d => d.Id == docId.Value);
                }
                
                // Note: Tag filters already applied above if hasTagFilters was true
                // Only apply here if no tag filters (shouldn't change anything, but kept for safety)
                if (!hasTagFilters)
                {
                    query = ApplyTagFilters(query, includeTags, excludeTags);
                }
                
                Console.WriteLine($"[SEARCH] Executing query...");
                var documents = await query.ToListAsync();
                
                Console.WriteLine($"[SEARCH] Fetched {documents.Count} documents with full details");
                
                // Build results with FTS5 scores, preserving FTS5 ranking order
                // Create position map ONCE outside the loop for efficiency
                Console.WriteLine($"[SEARCH] Creating position map...");
                var positionMap = documentIds.Select((id, index) => new { id, index }).ToDictionary(x => x.id, x => x.index);
                
                Console.WriteLine($"[SEARCH] Building results list...");
                var results = documents
                    .Select(d => new
                    {
                        Document = d,
                        Score = scoreMap.ContainsKey(d.Id) ? scoreMap[d.Id] : 0.0,
                        Position = positionMap.ContainsKey(d.Id) ? positionMap[d.Id] : int.MaxValue
                    })
                    .OrderBy(x => x.Position)
                    .Select(x => new
                    {
                        x.Document.Id,
                        x.Document.Name,
                        x.Document.FolderPath,
                        x.Document.SourceDrive,
                        x.Document.MimeType,
                        x.Document.Size,
                        x.Document.GoogleDriveFileId,
                        x.Document.WebViewLink,
                        x.Document.DownloadLink,
                        HasExtractedText = !string.IsNullOrEmpty(x.Document.ExtractedText),
                        ExtractedTextLength = x.Document.ExtractedText != null ? x.Document.ExtractedText.Length : 0,
                        Tags = x.Document.Tags.Select(t => t.TagName).ToList(),
                        x.Document.CreatedTime,
                        x.Document.ModifiedTime,
                        x.Document.LastModified,
                        x.Score
                    })
                    .ToList();
                
                Console.WriteLine($"[SEARCH] Built {results.Count} result objects");
                
                var response = new
                {
                    success = true,
                    query = q ?? "",
                    includeTags = includeTags ?? "",
                    excludeTags = excludeTags ?? "",
                    resultCount = results.Count,
                    totalCount = totalCount,
                    results = results
                };
                
                Console.WriteLine($"[SEARCH] Built response object with {results.Count} results");
                
                // Cache the response
                cache.Set(cacheKey, response, _cacheDuration);
                
                Console.WriteLine($"[SEARCH] Cached response, returning OK");
                
                return Results.Ok(response);
            }
            else
            {
                // No search query - just return with filters
                var query = context.JumpDocuments
                    .AsNoTracking()
                    .Include(d => d.Tags)
                    .AsQueryable();
                
                // Apply docId filter if provided
                if (docId.HasValue)
                {
                    query = query.Where(d => d.Id == docId.Value);
                }
                
                query = ApplyTagFilters(query, includeTags, excludeTags);
                
                var totalCount = await query.CountAsync();
                
                var results = await query
                    .OrderByDescending(d => d.LastModified)
                    .Skip(offset)
                    .Take(limit)
                    .Select(d => new
                    {
                        d.Id,
                        d.Name,
                        d.FolderPath,
                        d.SourceDrive,
                        d.MimeType,
                        d.Size,
                        d.GoogleDriveFileId,
                        d.WebViewLink,
                        d.DownloadLink,
                        HasExtractedText = !string.IsNullOrEmpty(d.ExtractedText),
                        ExtractedTextLength = d.ExtractedText != null ? d.ExtractedText.Length : 0,
                        Tags = d.Tags.Select(t => t.TagName).ToList(),
                        d.CreatedTime,
                        d.ModifiedTime,
                        d.LastModified,
                        Score = 0
                    })
                    .ToListAsync();
                
                var response = new
                {
                    success = true,
                    query = q ?? "",
                    includeTags = includeTags ?? "",
                    excludeTags = excludeTags ?? "",
                    resultCount = results.Count,
                    totalCount = totalCount,
                    results = results
                };
                
                // Cache the response
                cache.Set(cacheKey, response, _cacheDuration);
                
                return Results.Ok(response);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SEARCH ERROR] {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"[SEARCH ERROR] Stack: {ex.StackTrace}");
            
            // Also write to file for debugging
            try
            {
                System.IO.File.WriteAllText("search_error.txt", 
                    $"{DateTime.Now}\n{ex.GetType().Name}: {ex.Message}\n\nStack Trace:\n{ex.StackTrace}\n\nInner Exception:\n{ex.InnerException}");
            }
            catch { }
            
            return Results.BadRequest(new
            {
                success = false,
                error = ex.Message,
                type = ex.GetType().Name
            });
        }
    }

    private static string GenerateCacheKey(string? q, int limit, int offset, string? includeTags, string? excludeTags)
    {
        var sb = new StringBuilder("search:");
        sb.Append(q ?? "");
        sb.Append($":l{limit}");
        sb.Append($":o{offset}");
        if (!string.IsNullOrEmpty(includeTags))
            sb.Append($":i{includeTags}");
        if (!string.IsNullOrEmpty(excludeTags))
            sb.Append($":e{excludeTags}");
        return sb.ToString();
    }

    /// <summary>
    /// Parse search query with support for:
    /// - Regular terms: dragon age magic
    /// - Quoted phrases: "dragon age" magic
    /// - Excluded terms: drag -dragon (excludes documents with "dragon")
    /// </summary>
    private static (List<string> terms, List<string> excluded, List<string> phrases) ParseAdvancedSearchQuery(string? query)
    {
        var terms = new List<string>();
        var excluded = new List<string>();
        var phrases = new List<string>();
        
        if (string.IsNullOrWhiteSpace(query))
            return (terms, excluded, phrases);
        
        var currentTerm = new StringBuilder();
        bool inQuotes = false;
        bool isExcluded = false;
        
        for (int i = 0; i < query.Length; i++)
        {
            char c = query[i];
            
            if (c == '"')
            {
                if (inQuotes)
                {
                    // End of quoted phrase
                    if (currentTerm.Length > 0)
                    {
                        var phrase = currentTerm.ToString();
                        if (isExcluded)
                            excluded.Add(phrase);
                        else
                            phrases.Add(phrase);
                        currentTerm.Clear();
                        isExcluded = false;
                    }
                    inQuotes = false;
                }
                else
                {
                    // Save accumulated term first
                    if (currentTerm.Length > 0)
                    {
                        var term = currentTerm.ToString();
                        if (isExcluded)
                            excluded.Add(term);
                        else
                            terms.Add(term);
                        currentTerm.Clear();
                    }
                    inQuotes = true;
                }
            }
            else if (c == '-' && !inQuotes && currentTerm.Length == 0)
            {
                // Minus operator at start of term
                isExcluded = true;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                // Space outside quotes - end current term
                if (currentTerm.Length > 0)
                {
                    var term = currentTerm.ToString();
                    if (isExcluded)
                        excluded.Add(term);
                    else
                        terms.Add(term);
                    currentTerm.Clear();
                    isExcluded = false;
                }
            }
            else
            {
                // Regular character
                currentTerm.Append(c);
            }
        }
        
        // Add final term
        if (currentTerm.Length > 0)
        {
            var term = currentTerm.ToString();
            if (isExcluded)
                excluded.Add(term);
            else if (inQuotes)
                phrases.Add(term);
            else
                terms.Add(term);
        }
        
        return (
            terms.Where(t => !string.IsNullOrWhiteSpace(t)).ToList(),
            excluded.Where(t => !string.IsNullOrWhiteSpace(t)).ToList(),
            phrases.Where(t => !string.IsNullOrWhiteSpace(t)).ToList()
        );
    }

    /// <summary>
    /// Apply AND logic tag filtering
    /// - includeTags: Must have ALL these tags
    /// - excludeTags: Must NOT have ANY of these tags
    /// </summary>
    private static IQueryable<JumpDocument> ApplyTagFilters(
        IQueryable<JumpDocument> query,
        string? includeTags,
        string? excludeTags)
    {
        // Include tags (AND logic - must have ALL)
        if (!string.IsNullOrWhiteSpace(includeTags))
        {
            var includeTagList = includeTags.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim().ToLower())
                .ToList();
            
            foreach (var tag in includeTagList)
            {
                var tagLower = tag; // Capture for closure
                query = query.Where(d => d.Tags.Any(t => t.TagName.ToLower().Contains(tagLower)));
            }
        }
        
        // Exclude tags (NOT ANY - exclude if has any of these)
        if (!string.IsNullOrWhiteSpace(excludeTags))
        {
            var excludeTagList = excludeTags.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim().ToLower())
                .ToList();
            
            foreach (var tag in excludeTagList)
            {
                var tagLower = tag; // Capture for closure
                query = query.Where(d => !d.Tags.Any(t => t.TagName.ToLower().Contains(tagLower)));
            }
        }
        
        return query;
    }

    private static async Task<IResult> GetTagFrequencies(JumpChainDbContext context, SfwModeService? sfwMode = null)
    {
        try
        {
            var tagFrequencies = await context.DocumentTags
                .GroupBy(t => new { t.TagName, t.TagCategory })
                .Select(g => new {
                    TagName = g.Key.TagName,
                    TagCategory = g.Key.TagCategory,
                    Count = g.Count()
                })
                .OrderByDescending(t => t.Count)
                .ToListAsync();

            // Filter NSFW tags in SFW mode
            if (sfwMode?.IsSfwMode == true)
            {
                tagFrequencies = tagFrequencies
                    .Where(t => !sfwMode.IsNsfwTag(t.TagName))
                    .ToList();
            }

            var categorizedTags = tagFrequencies
                .GroupBy(t => t.TagCategory)
                .ToDictionary(g => g.Key, g => g.ToList());

            return Results.Ok(new {
                success = true,
                categorizedTags,
                totalTags = tagFrequencies.Count,
                allTags = tagFrequencies
            });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { 
                success = false, 
                error = ex.Message 
            });
        }
    }

    /// <summary>
    /// Batch fetch tags for multiple documents by their IDs.
    /// Used for SFW mode favorites filtering.
    /// GET /api/search/tags/batch?docIds=123,456,789
    /// Returns: { "Documents": [{ "DocumentId": 123, "Tags": ["Jump", "NSFW"] }] }
    /// </summary>
    private static async Task<IResult> GetDocumentTagsBatch(
        JumpChainDbContext context,
        string? docIds = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(docIds))
            {
                return Results.BadRequest(new { 
                    Success = false, 
                    Message = "docIds parameter is required" 
                });
            }

            // Parse comma-separated IDs
            var ids = docIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(id => int.TryParse(id.Trim(), out var num) ? num : -1)
                .Where(id => id > 0)
                .ToList();

            if (!ids.Any())
            {
                return Results.BadRequest(new { 
                    Success = false, 
                    Message = "No valid document IDs provided" 
                });
            }

            // Fetch documents with their tags
            var documents = await context.JumpDocuments
                .Where(d => ids.Contains(d.Id))
                .Include(d => d.Tags)
                .Select(d => new {
                    DocumentId = d.Id,
                    Tags = d.Tags.Select(t => t.TagName).ToList()
                })
                .ToListAsync();

            return Results.Ok(new { 
                Documents = documents 
            });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { 
                Success = false, 
                error = ex.Message 
            });
        }
    }

    /// <summary>
    /// Get random documents, optionally filtered by include/exclude tags and SFW mode
    /// </summary>
    private static async Task<IResult> GetRandomDocuments(
        JumpChainDbContext context,
        SfwModeService? sfwMode = null,
        int count = 10,
        string? includeTags = null,
        string? excludeTags = null)
    {
        try
        {
            var query = context.JumpDocuments.Include(d => d.Tags).Include(d => d.Urls).AsQueryable();
            
            // Apply SFW filtering if enabled
            if (sfwMode?.IsSfwMode == true)
            {
                query = query.Where(d => !d.Tags.Any(t => 
                    t.TagName == "NSFW" || 
                    t.TagName == "Lewd" || 
                    t.TagName == "NSFW-ish"));
            }
            
            // Apply include tag filters if specified
            if (!string.IsNullOrWhiteSpace(includeTags))
            {
                var includeTagList = includeTags.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim().ToLower()).ToList();
                
                foreach (var tag in includeTagList)
                {
                    query = query.Where(d => d.Tags.Any(t => t.TagName.ToLower().Contains(tag)));
                }
            }
            
            // Apply exclude tag filters if specified
            if (!string.IsNullOrWhiteSpace(excludeTags))
            {
                var excludeTagList = excludeTags.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim().ToLower()).ToList();
                
                foreach (var tag in excludeTagList)
                {
                    query = query.Where(d => !d.Tags.Any(t => t.TagName.ToLower().Contains(tag)));
                }
            }
            
            // Get all matching IDs
            var matchingIds = await query.Select(d => d.Id).ToListAsync();
            var totalCount = matchingIds.Count;
            
            // Cap count at available documents
            var actualCount = Math.Min(count, totalCount);
            
            if (actualCount == 0)
            {
                return Results.Ok(new
                {
                    Success = true,
                    Count = 0,
                    Documents = new List<object>()
                });
            }
            
            // Randomly select IDs in memory
            var random = new Random();
            var selectedIds = matchingIds.OrderBy(x => random.Next()).Take(actualCount).ToList();
            
            // Fetch full documents for selected IDs
            var randomDocuments = await context.JumpDocuments
                .Include(d => d.Tags)
                .Include(d => d.Urls)
                .Where(d => selectedIds.Contains(d.Id))
                .Select(d => new {
                    d.Id,
                    d.Name,
                    d.FolderPath,
                    d.SourceDrive,
                    d.MimeType,
                    d.Size,
                    d.WebViewLink,
                    d.CreatedTime,
                    d.ModifiedTime,
                    d.Description,
                    d.ThumbnailLink,
                    Tags = d.Tags.Select(t => t.TagName).ToList(),
                    Urls = d.Urls.Select(u => new {
                        u.SourceDrive,
                        u.FolderPath,
                        u.WebViewLink,
                        u.DownloadLink
                    }).ToList(),
                    HasMultipleUrls = d.Urls.Count > 1,
                    HasThumbnail = !string.IsNullOrWhiteSpace(d.ThumbnailLink),
                    HasExtractedText = !string.IsNullOrWhiteSpace(d.ExtractedText)
                })
                .ToListAsync();
            
            return Results.Ok(new
            {
                Success = true,
                Count = randomDocuments.Count,
                Documents = randomDocuments
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] GetRandomDocuments failed: {ex.Message}");
            return Results.Problem($"Error retrieving random documents: {ex.Message}");
        }
    }

    private static async Task<IResult> GetDocumentCount(IDocumentCountService documentCountService)
    {
        try
        {
            var count = await documentCountService.GetCountAsync();
            
            return Results.Ok(new {
                success = true,
                totalDocuments = count
            });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { 
                success = false, 
                error = ex.Message 
            });
        }
    }
}

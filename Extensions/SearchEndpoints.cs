using JumpChainSearch.Data;
using JumpChainSearch.Models;
using Microsoft.EntityFrameworkCore;

namespace JumpChainSearch.Extensions;

public static class SearchEndpoints
{
    public static RouteGroupBuilder MapSearchEndpoints(this RouteGroupBuilder group)
    {
        // Main search endpoint with tag filtering
        group.MapGet("/", FastSearch);
        
        // Random documents endpoint
        group.MapGet("/random", GetRandomDocuments);
        
        // Tag management endpoints
        group.MapGet("/tags", GetTagFrequencies);
        group.MapGet("/tags/batch", GetDocumentTagsBatch);
        group.MapGet("/browse-text", BrowseExtractedText);
        group.MapGet("/browse-text/{documentId:int}", GetDocumentText);
        
        return group;
    }

    private static async Task<IResult> FastSearch(
        JumpChainDbContext context, 
        string? q = null, 
        int limit = 50,
        int offset = 0,
        string? includeTags = null, 
        string? excludeTags = null,
        int? docId = null)
    {
        try
        {
            var query = context.JumpDocuments.Include(d => d.Tags).Include(d => d.Urls).AsQueryable();
            
            // Filter by specific document ID if provided
            if (docId.HasValue)
            {
                query = query.Where(d => d.Id == docId.Value);
                Console.WriteLine($"[DEBUG] Filtering by docId={docId.Value}");
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
            
            // Apply text search with relevance scoring
            List<dynamic>? scoredResults = null;
            int totalCount = 0;
            
            if (!string.IsNullOrWhiteSpace(q))
            {
                // Parse search query (handles quoted phrases)
                var searchTerms = ParseSearchQuery(q);
                
                // Build OR filter - document must match at least one term
                var matchedDocs = new List<int>(); // Document IDs that match
                
                foreach (var term in searchTerms)
                {
                    var termLower = term.ToLower();
                    var matchingIds = await query
                        .Where(d => 
                            d.Name.ToLower().Contains(termLower) ||
                            d.FolderPath.ToLower().Contains(termLower) ||
                            (d.ExtractedText != null && d.ExtractedText.ToLower().Contains(termLower)) ||
                            d.Tags.Any(t => t.TagName.ToLower().Contains(termLower)))
                        .Select(d => d.Id)
                        .ToListAsync();
                    
                    matchedDocs.AddRange(matchingIds);
                }
                
                // Get distinct matched document IDs
                var distinctMatchedIds = matchedDocs.Distinct().ToList();
                totalCount = distinctMatchedIds.Count;
                
                // Fetch and project documents directly (avoids loading Urls navigation property)
                var matchedDocuments = await query
                    .Where(d => distinctMatchedIds.Contains(d.Id))
                    .Select(d => new {
                        d.Id,
                        d.Name,
                        d.FolderPath,
                        d.SourceDrive,
                        d.MimeType,
                        d.Size,
                        d.GoogleDriveFileId,
                        d.WebViewLink,
                        d.DownloadLink,
                        d.ExtractedText,
                        d.LastModified,
                        Tags = d.Tags.Select(t => t.TagName).ToList()
                    })
                    .ToListAsync();
                
                // Calculate relevance scores and sort
                scoredResults = matchedDocuments
                    .Select(d => new {
                        Score = CalculateRelevanceScore(d, searchTerms),
                        Document = d
                    })
                    .OrderByDescending(x => x.Score)
                    .Skip(offset)
                    .Take(limit)
                    .Select(x => new {
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
                        Tags = x.Document.Tags,
                        LastModified = x.Document.LastModified,
                        RelevanceScore = x.Score
                    })
                    .Cast<dynamic>()
                    .ToList();
            }
            else
            {
                // No search query - just return with filters applied
                Console.WriteLine($"[DEBUG] No search query. Query count before pagination: {await query.CountAsync()}");
                totalCount = await query.CountAsync();
                
                var results = await query
                    .OrderByDescending(d => d.LastModified)
                    .Skip(offset)
                    .Take(limit)
                    .Select(d => new {
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
                        LastModified = d.LastModified,
                        RelevanceScore = 0.0
                    })
                    .ToListAsync();
                
                scoredResults = results.Cast<dynamic>().ToList();
            }
                
            return Results.Ok(new {
                success = true,
                query = q ?? "",
                includeTags = includeTags ?? "",
                excludeTags = excludeTags ?? "",
                resultCount = scoredResults?.Count ?? 0,
                totalCount = totalCount,
                results = scoredResults
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
    /// Parses a search query into individual terms and phrases.
    /// Handles quoted phrases and individual words.
    /// </summary>
    private static List<string> ParseSearchQuery(string query)
    {
        var terms = new List<string>();
        var currentTerm = new System.Text.StringBuilder();
        bool inQuotes = false;
        
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
                        terms.Add(currentTerm.ToString());
                        currentTerm.Clear();
                    }
                    inQuotes = false;
                }
                else
                {
                    // Start of quoted phrase - save any accumulated term first
                    if (currentTerm.Length > 0)
                    {
                        terms.Add(currentTerm.ToString());
                        currentTerm.Clear();
                    }
                    inQuotes = true;
                }
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                // Space outside quotes - end current term
                if (currentTerm.Length > 0)
                {
                    terms.Add(currentTerm.ToString());
                    currentTerm.Clear();
                }
            }
            else
            {
                // Regular character - add to current term
                currentTerm.Append(c);
            }
        }
        
        // Add final term if any
        if (currentTerm.Length > 0)
        {
            terms.Add(currentTerm.ToString());
        }
        
        return terms.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
    }
    
    /// <summary>
    /// Calculates relevance score for a document based on search terms.
    /// Higher scores indicate better matches.
    /// </summary>
    private static double CalculateRelevanceScore(JumpDocument doc, List<string> searchTerms)
    {
        double score = 0;
        
        string docName = doc.Name?.ToLower() ?? "";
        string docFolderPath = doc.FolderPath?.ToLower() ?? "";
        string docExtractedText = doc.ExtractedText?.ToLower() ?? "";
        var docTags = doc.Tags?.Select(t => t.TagName.ToLower()).ToList() ?? new List<string>();
        
        foreach (var term in searchTerms)
        {
            string termLower = term.ToLower();
            
            // Title matches are worth the most (1000 points per match)
            score += CountOccurrences(docName, termLower) * 1000;
            
            // Folder path matches are worth medium points (100 points per match)
            score += CountOccurrences(docFolderPath, termLower) * 100;
            
            // Tag matches are worth good points (500 points per match)
            foreach (var tag in docTags)
            {
                score += CountOccurrences(tag, termLower) * 500;
            }
            
            // Content matches are worth least (1 point per match)
            score += CountOccurrences(docExtractedText, termLower) * 1;
            
            // Bonus for exact title match
            if (docName == termLower)
            {
                score += 10000;
            }
            
            // Bonus for title starts with term
            if (docName.StartsWith(termLower))
            {
                score += 5000;
            }
        }
        
        return score;
    }
    
    /// <summary>
    /// Calculates relevance score for a projected document (anonymous type from query).
    /// </summary>
    private static double CalculateRelevanceScore(dynamic doc, List<string> searchTerms)
    {
        double score = 0;
        
        string docName = ((string)doc.Name ?? "").ToLower();
        string docFolderPath = ((string)doc.FolderPath ?? "").ToLower();
        string docExtractedText = ((string)doc.ExtractedText ?? "").ToLower();
        var docTags = ((List<string>)doc.Tags ?? new List<string>()).Select(t => t.ToLower()).ToList();
        
        foreach (var term in searchTerms)
        {
            string termLower = term.ToLower();
            
            // Title matches are worth the most (1000 points per match)
            score += CountOccurrences(docName, termLower) * 1000;
            
            // Folder path matches are worth medium points (100 points per match)
            score += CountOccurrences(docFolderPath, termLower) * 100;
            
            // Tag matches are worth good points (500 points per match)
            foreach (var tag in docTags)
            {
                score += CountOccurrences(tag, termLower) * 500;
            }
            
            // Content matches are worth least (1 point per match)
            score += CountOccurrences(docExtractedText, termLower) * 1;
            
            // Bonus for exact title match
            if (docName == termLower)
            {
                score += 10000;
            }
            
            // Bonus for title starts with term
            if (docName.StartsWith(termLower))
            {
                score += 5000;
            }
        }
        
        return score;
    }
    
    /// <summary>
    /// Counts case-insensitive occurrences of a search term in text.
    /// </summary>
    private static int CountOccurrences(string text, string term)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(term))
            return 0;
        
        int count = 0;
        int index = 0;
        
        while ((index = text.IndexOf(term, index, StringComparison.OrdinalIgnoreCase)) != -1)
        {
            count++;
            index += term.Length;
        }
        
        return count;
    }

    /// <summary>
    /// Get random documents, optionally filtered by include/exclude tags
    /// </summary>
    private static async Task<IResult> GetRandomDocuments(
        JumpChainDbContext context,
        int count = 10,
        string? includeTags = null,
        string? excludeTags = null)
    {
        try
        {
            var query = context.JumpDocuments.Include(d => d.Tags).Include(d => d.Urls).AsQueryable();
            
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
            
            // Get total count after filters
            var totalCount = await query.CountAsync();
            
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
            
            // Use OrderBy(r => EF.Functions.Random()) for SQLite
            var randomDocuments = await query
                .OrderBy(d => Guid.NewGuid())
                .Take(actualCount)
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

    private static async Task<IResult> GetTagFrequencies(JumpChainDbContext context)
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

    private static async Task<IResult> BrowseExtractedText(
        JumpChainDbContext context, 
        int page = 1, 
        int limit = 10, 
        string? search = "", 
        string? sortBy = "id", 
        string? sortOrder = "asc", 
        bool? hasText = null, 
        string? extractionMethod = "")
    {
        try
        {
            var query = context.JumpDocuments.AsQueryable();
            
            // Filter by extraction status
            if (hasText == true)
            {
                query = query.Where(d => !string.IsNullOrEmpty(d.ExtractedText));
            }
            else if (hasText == false)
            {
                query = query.Where(d => string.IsNullOrEmpty(d.ExtractedText));
            }
            
            // Search filter
            if (!string.IsNullOrWhiteSpace(search))
            {
                var searchTerm = search.ToLower();
                query = query.Where(d => 
                    d.Name.ToLower().Contains(searchTerm) ||
                    (d.ExtractedText != null && d.ExtractedText.ToLower().Contains(searchTerm)));
            }
            
            // Sorting
            query = sortBy?.ToLower() switch
            {
                "name" => sortOrder == "desc" 
                    ? query.OrderByDescending(d => d.Name) 
                    : query.OrderBy(d => d.Name),
                "size" => sortOrder == "desc" 
                    ? query.OrderByDescending(d => d.Size) 
                    : query.OrderBy(d => d.Size),
                "textlength" => sortOrder == "desc" 
                    ? query.OrderByDescending(d => d.ExtractedText!.Length) 
                    : query.OrderBy(d => d.ExtractedText!.Length),
                _ => sortOrder == "desc" 
                    ? query.OrderByDescending(d => d.Id) 
                    : query.OrderBy(d => d.Id)
            };
            
            var totalCount = await query.CountAsync();
            var totalPages = (int)Math.Ceiling((double)totalCount / limit);
            
            var documents = await query
                .Skip((page - 1) * limit)
                .Take(limit)
                .Select(d => new {
                    d.Id,
                    d.Name,
                    d.MimeType,
                    d.Size,
                    HasExtractedText = !string.IsNullOrEmpty(d.ExtractedText),
                    ExtractedTextLength = d.ExtractedText != null ? d.ExtractedText.Length : 0,
                    ExtractedTextPreview = d.ExtractedText != null && d.ExtractedText.Length > 0
                        ? d.ExtractedText.Substring(0, Math.Min(200, d.ExtractedText.Length)) + "..."
                        : "No text extracted"
                })
                .ToListAsync();

            return Results.Ok(new {
                success = true,
                page,
                limit,
                totalCount,
                totalPages,
                hasNext = page < totalPages,
                hasPrevious = page > 1,
                documents
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

    private static async Task<IResult> GetDocumentText(JumpChainDbContext context, int documentId)
    {
        try
        {
            var document = await context.JumpDocuments
                .Where(d => d.Id == documentId)
                .Select(d => new {
                    d.Id,
                    d.Name,
                    d.MimeType,
                    d.Size,
                    d.FolderPath,
                    d.SourceDrive,
                    HasExtractedText = !string.IsNullOrEmpty(d.ExtractedText),
                    ExtractedTextLength = d.ExtractedText != null ? d.ExtractedText.Length : 0,
                    ExtractedText = d.ExtractedText
                })
                .FirstOrDefaultAsync();

            if (document == null)
            {
                return Results.NotFound(new { success = false, message = "Document not found" });
            }

            return Results.Ok(new {
                success = true,
                document
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
using Microsoft.EntityFrameworkCore;
using JumpChainSearch.Data;
using JumpChainSearch.Models;
using JumpChainSearch.Services;
using System.Text.Json;

namespace JumpChainSearch.Extensions;

/// <summary>
/// Endpoints for parsing and searching purchasable items from JumpChain documents
/// </summary>
public static class PurchasableEndpoints
{
    public static RouteGroupBuilder MapPurchasableEndpoints(this RouteGroupBuilder group)
    {
        // Main parsing and search endpoints
        group.MapPost("/parse/{documentId:int}", ParsePurchasables);
        group.MapPost("/parse-batch", ParsePurchasablesBatch);
        group.MapGet("/{documentId:int}", GetPurchasables);
        group.MapGet("/search", SearchPurchasables);
        
        // Debug endpoints
        group.MapGet("/debug/raw-text/{documentId:int}", DebugRawText);
        group.MapGet("/debug/parsing/{documentId:int}", DebugParsing);
        group.MapGet("/debug/service/{documentId:int}", DebugParserService);
        group.MapGet("/debug/simple-parse/{documentId:int}", DebugSimpleParse);
        group.MapGet("/debug/format-analysis/{documentId:int}", DebugFormatAnalysis);
        group.MapGet("/debug/text/{documentId:int}", DebugDocumentText);
        
        return group;
    }

    /// <summary>
    /// Parse purchasables from a single document
    /// </summary>
    private static async Task<IResult> ParsePurchasables(
        JumpChainDbContext context, 
        IPurchasableParsingService parsingService, 
        int documentId)
    {
        try
        {
            var document = await context.JumpDocuments
                .Where(d => d.Id == documentId && !string.IsNullOrEmpty(d.ExtractedText))
                .FirstOrDefaultAsync();
            
            if (document == null)
            {
                return Results.NotFound(new { success = false, message = "Document not found or has no extracted text" });
            }

            var count = await parsingService.ParseAndSaveDocumentAsync(document);
            
            return Results.Ok(new {
                success = true,
                message = $"Parsed {count} purchasables from document '{document.Name}'",
                documentId = document.Id,
                documentName = document.Name ?? "Unknown",
                purchasablesFound = count
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
    /// Parse purchasables from multiple documents
    /// </summary>
    private static async Task<IResult> ParsePurchasablesBatch(
        JumpChainDbContext context, 
        IPurchasableParsingService parsingService, 
        HttpRequest request)
    {
        try
        {
            var body = await new StreamReader(request.Body).ReadToEndAsync();
            var requestData = JsonSerializer.Deserialize<Dictionary<string, object>>(body);
            
            if (requestData == null)
            {
                return Results.BadRequest(new { success = false, message = "Invalid request body" });
            }
            
            var documentIds = new List<int>();
            if (requestData.ContainsKey("documentIds"))
            {
                var idsArray = (JsonElement)requestData["documentIds"];
                documentIds = idsArray.EnumerateArray().Select(x => x.GetInt32()).ToList();
            }
            else if (requestData.ContainsKey("filter"))
            {
                var filter = requestData["filter"].ToString();
                if (filter == "with_text")
                {
                    documentIds = await context.JumpDocuments
                        .Where(d => !string.IsNullOrEmpty(d.ExtractedText))
                        .Select(d => d.Id)
                        .Take(10) // Limit to 10 for testing
                        .ToListAsync();
                }
            }

            if (!documentIds.Any())
            {
                return Results.BadRequest(new { success = false, message = "No document IDs provided" });
            }

            var totalParsed = await parsingService.ParseMultipleDocumentsAsync(documentIds);
            
            return Results.Ok(new {
                success = true,
                message = $"Parsed purchasables from {documentIds.Count} documents",
                documentsProcessed = documentIds.Count,
                totalPurchasablesFound = totalParsed
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
    /// Get all purchasables for a specific document
    /// </summary>
    private static async Task<IResult> GetPurchasables(JumpChainDbContext context, int documentId)
    {
        try
        {
            var rawPurchasables = await context.DocumentPurchasables
                .Where(p => p.JumpDocumentId == documentId)
                .OrderBy(p => p.Category)
                .ThenBy(p => p.Name)
                .Select(p => new {
                    p.Id,
                    p.Name,
                    p.Category,
                    p.Description,
                    p.CostsJson,
                    p.PrimaryCost,
                    p.LineNumber
                })
                .ToListAsync();

            var purchasables = rawPurchasables.Select(p => new {
                p.Id,
                p.Name,
                p.Category,
                p.Description,
                Costs = JsonSerializer.Deserialize<List<PurchasableCost>>(p.CostsJson ?? "[]"),
                p.PrimaryCost,
                p.LineNumber
            }).ToList();

            var groupedByCategory = purchasables
                .GroupBy(p => p.Category)
                .ToDictionary(g => g.Key ?? "Unknown", g => g.ToList());

            return Results.Ok(new {
                success = true,
                documentId,
                totalPurchasables = purchasables.Count,
                categories = groupedByCategory.Keys,
                purchasablesByCategory = groupedByCategory
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
    /// Search purchasables across all documents
    /// </summary>
    private static async Task<IResult> SearchPurchasables(
        JumpChainDbContext context, 
        string? search = "", 
        string? category = "", 
        int? minCost = null, 
        int? maxCost = null, 
        int page = 1, 
        int limit = 20)
    {
        try
        {
            var query = context.DocumentPurchasables
                .Include(p => p.JumpDocument)
                .AsQueryable();

            // Apply filters with null safety
            var searchTerm = search ?? "";
            var categoryFilter = category ?? "";
            
            if (!string.IsNullOrEmpty(searchTerm))
            {
                query = query.Where(p => 
                    (p.Name != null && p.Name.Contains(searchTerm)) || 
                    (p.Description != null && p.Description.Contains(searchTerm)));
            }

            if (!string.IsNullOrEmpty(categoryFilter))
            {
                query = query.Where(p => p.Category == categoryFilter);
            }

            if (minCost.HasValue)
            {
                query = query.Where(p => p.PrimaryCost >= minCost.Value);
            }

            if (maxCost.HasValue)
            {
                query = query.Where(p => p.PrimaryCost <= maxCost.Value);
            }

            var totalCount = await query.CountAsync();
            var totalPages = (int)Math.Ceiling((double)totalCount / limit);

            var rawPurchasables = await query
                .OrderBy(p => p.Category)
                .ThenBy(p => p.PrimaryCost)
                .ThenBy(p => p.Name)
                .Skip((page - 1) * limit)
                .Take(limit)
                .Select(p => new {
                    p.Id,
                    p.Name,
                    p.Category,
                    p.Description,
                    p.CostsJson,
                    p.PrimaryCost,
                    p.LineNumber,
                    Document = new {
                        p.JumpDocument.Id,
                        p.JumpDocument.Name,
                        p.JumpDocument.SourceDrive
                    }
                })
                .ToListAsync();

            var purchasables = rawPurchasables.Select(p => new {
                p.Id,
                p.Name,
                p.Category,
                p.Description,
                Costs = JsonSerializer.Deserialize<List<PurchasableCost>>(p.CostsJson ?? "[]"),
                p.PrimaryCost,
                p.LineNumber,
                p.Document
            }).ToList();

            return Results.Ok(new {
                success = true,
                page,
                limit,
                totalCount,
                totalPages,
                hasNext = page < totalPages,
                hasPrevious = page > 1,
                purchasables
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

    // ===== DEBUG ENDPOINTS =====

    /// <summary>
    /// View raw document text (first 2000 characters)
    /// </summary>
    private static async Task<IResult> DebugRawText(JumpChainDbContext context, int documentId)
    {
        var document = await context.JumpDocuments.FindAsync(documentId);
        if (document == null) 
            return Results.NotFound(new { error = "Document not found" });
        
        var extractedText = document.ExtractedText ?? "";
        var textStart = extractedText.Length > 2000 ? extractedText.Substring(0, 2000) : extractedText;
        return Results.Text(textStart, "text/plain");
    }

    /// <summary>
    /// Test parsing patterns on document lines
    /// </summary>
    private static async Task<IResult> DebugParsing(JumpChainDbContext context, int documentId)
    {
        var document = await context.JumpDocuments.FindAsync(documentId);
        if (document == null) 
            return Results.NotFound(new { error = "Document not found" });
        
        var extractedText = document.ExtractedText ?? "";
        var lines = extractedText.Split('\n', StringSplitOptions.None);
        var debugResults = new List<object>();
        
        // Test our parsing patterns on each line
        for (int i = 0; i < Math.Min(lines.Length, 50); i++) // Limit to first 50 lines
        {
            var line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;
            
            // Test Pattern 1: Standard JumpChain format "Name (+100 CP)"
            var jumpChainPattern = @"^(.+?)\s*\(([+\-]?\d+)\s*CP\)";
            var match1 = System.Text.RegularExpressions.Regex.Match(line, jumpChainPattern, 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            // Test Pattern 2: Item name followed by colon "Item Name: Description"
            var colonPattern = @"^([^:]+):\s*(.+)";
            var match2 = System.Text.RegularExpressions.Regex.Match(line, colonPattern);
            
            if (match1.Success || match2.Success)
            {
                debugResults.Add(new
                {
                    lineNumber = i + 1,
                    text = line,
                    jumpChainMatch = match1.Success ? new { name = match1.Groups[1].Value.Trim(), cost = match1.Groups[2].Value } : null,
                    colonMatch = match2.Success ? new { name = match2.Groups[1].Value.Trim(), description = match2.Groups[2].Value.Trim() } : null
                });
            }
        }
        
        return Results.Json(debugResults);
    }

    /// <summary>
    /// Test actual parsing service on a document
    /// </summary>
    private static async Task<IResult> DebugParserService(
        JumpChainDbContext context, 
        IPurchasableParsingService parsingService, 
        int documentId)
    {
        var document = await context.JumpDocuments.FindAsync(documentId);
        if (document == null) 
            return Results.NotFound(new { error = "Document not found" });
        
        // Call the actual parsing service
        var purchasables = await parsingService.ParseDocumentAsync(document);
        
        var extractedText = document.ExtractedText ?? "";
        return Results.Json(new
        {
            documentName = document.Name ?? "Unknown",
            totalLines = extractedText.Split('\n').Length,
            purchasablesFound = purchasables.Count,
            purchasables = purchasables.Take(10).Select(p => new
            {
                name = p.Name ?? "Unknown",
                category = p.Category ?? "Unknown",
                cost = p.PrimaryCost,
                lineNumber = p.LineNumber,
                description = p.Description != null && p.Description.Length > 100 
                    ? p.Description.Substring(0, 100) + "..." 
                    : p.Description ?? ""
            })
        });
    }

    /// <summary>
    /// Test parsing known good lines
    /// </summary>
    private static Task<IResult> DebugSimpleParse(JumpChainDbContext context, int documentId)
    {
        // Test parsing specific known good lines manually
        var testLines = new[]
        {
            "CRT Goggles (+100 CP)",
            "Double Trouble (+100 CP)", 
            "Ring Modus (+100 CP)"
        };
        
        var results = new List<object>();
        
        foreach (var testLine in testLines)
        {
            // Test the JumpChain pattern directly
            var pattern = @"^(.+?)\s*\(([+\-]?\d+)\s*CP\)";
            var match = System.Text.RegularExpressions.Regex.Match(testLine, pattern, 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            if (match.Success)
            {
                var name = match.Groups[1].Value.Trim();
                var costStr = match.Groups[2].Value;
                var costValue = int.Parse(costStr.Replace("+", "").Replace("-", ""));
                
                results.Add(new
                {
                    testLine = testLine,
                    matched = true,
                    name = name,
                    cost = costValue,
                    validation = new
                    {
                        nameLength = name.Length,
                        nameValid = !string.IsNullOrWhiteSpace(name) && name.Length >= 2 && name.Length <= 100
                    }
                });
            }
            else
            {
                results.Add(new
                {
                    testLine = testLine,
                    matched = false
                });
            }
        }
        
        return Task.FromResult(Results.Json(results));
    }

    /// <summary>
    /// Analyze document format to determine parsing strategy
    /// </summary>
    private static async Task<IResult> DebugFormatAnalysis(JumpChainDbContext context, int documentId)
    {
        var document = await context.JumpDocuments.FindAsync(documentId);
        if (document == null) 
            return Results.NotFound(new { error = "Document not found" });
        
        // Simulate the format analysis
        var extractedText = document.ExtractedText ?? "";
        var lines = extractedText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        int jumpChainCPCount = 0;
        int colonDelimitedCount = 0;
        int totalLines = 0;
        var sampleJumpChain = new List<string>();
        var sampleColon = new List<string>();

        foreach (var line in lines.Take(100))
        {
            var trimmed = line.Trim();
            if (trimmed.Length < 10) continue;
            totalLines++;

            // Pattern 1: JumpChain standard
            if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^.+\s*\([+\-]?\d+\s*CP\)", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                jumpChainCPCount++;
                if (sampleJumpChain.Count < 5) sampleJumpChain.Add(trimmed);
            }

            // Pattern 2: Colon-delimited
            var colonMatch = System.Text.RegularExpressions.Regex.Match(trimmed, @"^([A-Za-z][A-Za-z0-9\s\-']{2,50}):\s*(.{10,})");
            if (colonMatch.Success)
            {
                var itemName = colonMatch.Groups[1].Value.Trim();
                var lower = itemName.ToLowerInvariant();
                bool isValid = itemName.Length >= 3 && itemName.Length <= 50 && 
                              char.IsUpper(itemName[0]) && 
                              !lower.Contains("will be") && !lower.Contains("this") && 
                              !lower.StartsWith("in ") && !lower.StartsWith("by ");
                
                if (isValid)
                {
                    colonDelimitedCount++;
                    if (sampleColon.Count < 5) sampleColon.Add(trimmed);
                }
            }
        }

        var jumpChainRatio = totalLines > 0 ? (double)jumpChainCPCount / totalLines : 0;
        var colonRatio = totalLines > 0 ? (double)colonDelimitedCount / totalLines : 0;

        string formatType;
        double confidence;
        
        if (jumpChainCPCount >= 3 && jumpChainRatio > colonRatio)
        {
            formatType = "JumpChainStandard";
            confidence = Math.Min(0.95, 0.5 + jumpChainRatio);
        }
        else if (colonDelimitedCount >= 3 && colonRatio > jumpChainRatio)
        {
            formatType = "ColonDelimited";
            confidence = Math.Min(0.95, 0.5 + colonRatio);
        }
        else if (jumpChainCPCount >= 1 && colonDelimitedCount >= 1)
        {
            formatType = "Mixed";
            confidence = 0.7;
        }
        else
        {
            formatType = "Unknown";
            confidence = 0.3;
        }

        return Results.Json(new
        {
            documentName = document.Name ?? "Unknown",
            formatType = formatType,
            confidence = confidence,
            analysis = new
            {
                totalAnalyzedLines = totalLines,
                jumpChainPatternCount = jumpChainCPCount,
                colonPatternCount = colonDelimitedCount,
                jumpChainRatio = jumpChainRatio,
                colonRatio = colonRatio
            },
            samples = new
            {
                jumpChainLines = sampleJumpChain,
                colonLines = sampleColon
            }
        });
    }

    /// <summary>
    /// Get complete document text for debugging
    /// </summary>
    private static async Task<IResult> DebugDocumentText(JumpChainDbContext context, int documentId)
    {
        try
        {
            var document = await context.JumpDocuments
                .Where(d => d.Id == documentId)
                .Select(d => new { d.Id, d.Name, d.ExtractedText })
                .FirstOrDefaultAsync();

            if (document == null)
            {
                return Results.NotFound(new { success = false, error = "Document not found" });
            }

            var extractedText = document.ExtractedText ?? "";
            return Results.Ok(new {
                success = true,
                documentId = document.Id,
                name = document.Name ?? "Unknown",
                extractedText = extractedText,
                textLength = extractedText.Length
            });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { success = false, error = ex.Message });
        }
    }
}

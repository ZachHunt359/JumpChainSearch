using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using JumpChainSearch.Data;

namespace JumpChainSearch.Extensions;

public static class RedirectEndpoints
{
    public static WebApplication MapRedirectEndpoints(this WebApplication app)
    {
        // Health check endpoint for reconnect modal
        app.MapGet("/health", () => Results.Ok("OK"));

        // Tag management redirects
        app.MapGet("/debug-tag-inconsistencies", () => Results.Redirect("/api/tags/debug-inconsistencies"));
        app.MapPost("/fix-tag-inconsistencies", () => Results.Redirect("/api/tags/fix-inconsistencies"));
        app.MapGet("/tags", () => Results.Redirect("/api/tags"));
        app.MapGet("/test-add-tags", () => Results.Redirect("/api/tags/test-add"));
        app.MapPost("/refresh-tags", () => Results.Redirect("/api/tags/refresh"));
        app.MapGet("/batch-refresh-tags/{batchNumber:int?}", (int? batchNumber) => Results.Redirect($"/api/tags/batch-refresh/{batchNumber}"));
        app.MapPost("/complete-all-tags", () => Results.Redirect("/api/tags/complete-all"));
        app.MapGet("/debug-untagged/{limit:int?}", (int? limit) => Results.Redirect($"/api/tags/debug-untagged/{limit}"));
        app.MapGet("/methodical-tag-test", () => Results.Redirect("/api/tags/methodical-test"));
        app.MapGet("/test-tag-one-document/{documentId:int}", (int documentId) => Results.Redirect($"/api/tags/test-one/{documentId}"));
        app.MapGet("/methodical-fresh-context-test", () => Results.Redirect("/api/tags/methodical-fresh-context"));
        app.MapGet("/test-comprehensive-save/{documentId:int}", (int documentId) => Results.Redirect($"/api/tags/test-comprehensive-save/{documentId}"));
        app.MapGet("/debug-comprehensive-tagging/{documentId:int}", (int documentId) => Results.Redirect($"/api/tags/debug-comprehensive/{documentId}"));
        app.MapPost("/batch-simple-tagging", () => Results.Redirect("/api/tags/batch-simple"));
        app.MapPost("/batch-comprehensive-tagging", () => Results.Redirect("/api/tags/batch-comprehensive"));
        app.MapPost("/remove-sfw-tags", () => Results.Redirect("/api/tags/remove-sfw"));

        // Primary search endpoint with tag filtering
        app.MapGet("/search", async (JumpChainDbContext context, string? q = "", int limit = 50, string? includeTags = "", string? excludeTags = "") =>
        {
            try
            {
                var query = context.JumpDocuments.Include(d => d.Tags).Include(d => d.Urls).AsQueryable();
                if (!string.IsNullOrWhiteSpace(includeTags))
                {
                    var requiredTags = includeTags.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                                 .Select(t => t.Trim())
                                                 .Where(t => !string.IsNullOrEmpty(t))
                                                 .ToList();
                    if (requiredTags.Any())
                    {
                        foreach (var tag in requiredTags)
                        {
                            query = query.Where(d => d.Tags.Any(t => t.TagName == tag));
                        }
                    }
                }
                if (!string.IsNullOrWhiteSpace(excludeTags))
                {
                    var excludedTags = excludeTags.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                                 .Select(t => t.Trim())
                                                 .Where(t => !string.IsNullOrEmpty(t))
                                                 .ToList();
                    if (excludedTags.Any())
                    {
                        foreach (var tag in excludedTags)
                        {
                            query = query.Where(d => !d.Tags.Any(t => t.TagName == tag));
                        }
                    }
                }
                if (!string.IsNullOrWhiteSpace(q))
                {
                    query = query.Where(d =>
                        EF.Functions.Like(d.Name.ToLower(), $"%{q.ToLower()}%") ||
                        EF.Functions.Like(d.FolderPath.ToLower(), $"%{q.ToLower()}%") ||
                        EF.Functions.Like(d.Description.ToLower(), $"%{q.ToLower()}%") ||
                        d.Tags.Any(t => EF.Functions.Like(t.TagName.ToLower(), $"%{q.ToLower()}%"))
                    );
                }
                var results = await query
                    .OrderBy(d => d.Name)
                    .Take(limit)
                    .Select(d => new {
                        id = d.Id,
                        name = d.Name,
                        folderPath = d.FolderPath,
                        sourceDrive = d.SourceDrive,
                        mimeType = d.MimeType,
                        size = d.Size,
                        webViewLink = d.WebViewLink,
                        createdTime = d.CreatedTime,
                        modifiedTime = d.ModifiedTime,
                        tags = d.Tags.Select(t => t.TagName).ToList(),
                        urls = d.Urls.Select(u => new {
                            sourceDrive = u.SourceDrive,
                            folderPath = u.FolderPath,
                            webViewLink = u.WebViewLink,
                            downloadLink = u.DownloadLink
                        }).ToList(),
                        hasMultipleUrls = d.Urls.Count() > 0,
                        thumbnailLink = d.ThumbnailLink,
                        hasThumbnail = d.HasThumbnail
                    })
                    .ToListAsync();
                return Results.Ok(new {
                    success = true,
                    query = q,
                    includeTags = includeTags,
                    excludeTags = excludeTags,
                    resultCount = results.Count,
                    results
                });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new {
                    success = false,
                    error = ex.Message
                });
            }
        });

        // Text extraction redirects
        app.MapGet("/get-extracted-text/{documentId:int}", (int documentId) => Results.Redirect($"/api/text/{documentId}"));
        app.MapGet("/debug-extract/{fileId}", (string fileId) => Results.Redirect($"/api/text/debug/{fileId}"));
        app.MapPost("/test-save-text/{documentId:int}", (int documentId) => Results.Redirect($"/api/text/test-save/{documentId}"));
        app.MapGet("/test-text-extraction/{documentId:int}", (int documentId) => Results.Redirect($"/api/text/test/{documentId}"));
        app.MapPost("/bulk-extract-text", () => Results.Redirect("/api/text/bulk"));
        app.MapGet("/debug-extraction-status", () => Results.Redirect("/api/text/status"));
        app.MapPost("/test-extract-few/{limit:int?}", () => Results.Redirect("/api/text/test-few"));
        app.MapGet("/diagnose-document/{documentId:int}", (int documentId) => Results.Redirect($"/api/text/diagnose/{documentId}"));
        app.MapPost("/extract-and-save/{documentId:int}", (int documentId) => Results.Redirect($"/api/text/extract-save/{documentId}"));
        app.MapGet("/direct-drive-export/{fileId}", (string fileId) => Results.Redirect($"/api/text/direct-export/{fileId}"));
        app.MapPost("/re-extract-with-improved-methods", () => Results.Redirect("/api/text/re-extract-all"));
        app.MapPost("/re-extract-document/{documentId:int}", (int documentId) => Results.Redirect($"/api/text/re-extract/{documentId}"));
        app.MapGet("/check-extraction-status/{limit:int?}", () => Results.Redirect("/api/text/check-status"));
        app.MapGet("/batch-extract-with-tracking", () => Results.Redirect("/api/text/batch-tracking"));
        app.MapGet("/get-ocr-candidates", () => Results.Redirect("/api/text/ocr-candidates"));
        app.MapGet("/analyze-zero-filesize", () => Results.Redirect("/api/text/analyze-zero-filesize"));

        // Purchasable debugging redirects
        app.MapGet("/debug-raw-text/{documentId:int}", (int documentId) => Results.Redirect($"/api/purchasables/debug/raw-text/{documentId}"));
        app.MapGet("/debug-parsing/{documentId:int}", (int documentId) => Results.Redirect($"/api/purchasables/debug/parsing/{documentId}"));
        app.MapGet("/debug-parser-service/{documentId:int}", (int documentId) => Results.Redirect($"/api/purchasables/debug/service/{documentId}"));
        app.MapGet("/debug-simple-parse/{documentId:int}", (int documentId) => Results.Redirect($"/api/purchasables/debug/simple-parse/{documentId}"));
        app.MapGet("/debug-format-analysis/{documentId:int}", (int documentId) => Results.Redirect($"/api/purchasables/debug/format-analysis/{documentId}"));
        app.MapGet("/debug-document-text/{documentId:int}", (int documentId) => Results.Redirect($"/api/purchasables/debug/text/{documentId}"));

        return app;
    }
}

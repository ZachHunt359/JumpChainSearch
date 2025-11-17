using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using System.Text;
using JumpChainSearch.Data;
using JumpChainSearch.Models;
using JumpChainSearch.Services;
using JumpChainSearch.Extensions;
using JumpChainSearch.Helpers;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

// Load environment variables from .env file manually
try
{
    if (File.Exists(".env"))
    {
        var lines = File.ReadAllLines(".env");
        foreach (var line in lines)
        {
            if (!line.StartsWith("#") && line.Contains("="))
            {
                var parts = line.Split('=', 2);
                if (parts.Length == 2)
                {
                    Environment.SetEnvironmentVariable(parts[0].Trim(), parts[1].Trim());
                }
            }
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Warning: Could not load .env file: {ex.Message}");
}


// Handle CLI commands (admin user creation, etc.)
var cliResult = await CliAdminCommands.Handle(args);
if (cliResult >= 0) return cliResult;

var builder = WebApplication.CreateBuilder(args);

// Determine connection string (fallback to SQLite file if not set)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
                       ?? builder.Configuration["ConnectionStrings:DefaultConnection"]
                       ?? Environment.GetEnvironmentVariable("DefaultConnection")
                       ?? Environment.GetEnvironmentVariable("CONNECTION_STRING")
                       ?? "Data Source=jumpchain.db";

// Register core services
builder.Services.AddMemoryCache();
builder.Services.AddJumpChainServices(connectionString);

// Register a scoped HttpClient for server-side Blazor components that inject it.
// Use NavigationManager.BaseUri as the BaseAddress so relative URLs like "/api/..." work.
builder.Services.AddScoped(sp =>
{
    var nav = sp.GetService<NavigationManager>();
    var baseUri = nav?.BaseUri ?? new Uri("http://localhost").ToString();
    return new System.Net.Http.HttpClient { BaseAddress = new Uri(baseUri) };
});

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor(options =>
{
    // Improve reconnection experience
    options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(3);
    options.DisconnectedCircuitMaxRetained = 100;
    options.JSInteropDefaultCallTimeout = TimeSpan.FromMinutes(2);
    options.MaxBufferedUnacknowledgedRenderBatches = 10;
})
.AddHubOptions(options =>
{
    // SignalR configuration for better mobile experience
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(60); // Default is 30s
    options.HandshakeTimeout = TimeSpan.FromSeconds(30); // Default is 15s
    options.KeepAliveInterval = TimeSpan.FromSeconds(15); // Default is 15s
    options.MaximumReceiveMessageSize = 64 * 1024; // 64KB
});

// Add Swagger/OpenAPI support for API testing
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "JumpChain Search API",
        Version = "v1",
        Description = "API for searching and managing JumpChain documents across Google Drives"
    });
    
    // Only document endpoints under /api/* and /admin/* to avoid conflicts with redirects
    options.DocInclusionPredicate((docName, apiDesc) =>
    {
        var routeTemplate = apiDesc.RelativePath ?? "";
        return routeTemplate.StartsWith("api/") || routeTemplate.StartsWith("admin/");
    });
});

// Build the app
var app = builder.Build();

// Enable Swagger in all environments (useful for testing on VPS)
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "JumpChain Search API v1");
    options.RoutePrefix = "swagger"; // Access at /swagger
    options.DocumentTitle = "JumpChain Search API";
});

// Serve static files from wwwroot (CSS, JS, images)
app.UseStaticFiles();

// Ensure routing is enabled for endpoint mappings
app.UseRouting();

// Map the Blazor Server SignalR hub so the client script is available
app.MapBlazorHub();

// ===== REDIRECT AND AD-HOC ENDPOINTS =====
app.MapRedirectEndpoints();
// All new code should use the organized /api/* endpoints

// Map the organized API route groups (search, tags, text, etc.)
// This registers endpoints such as `/api/search`, `/api/search/tags`, and `/api/search/count`.
app.MapGroup("/api/search").MapOptimizedSearchEndpoints();
// Map text browser UI endpoints (e.g. /api/browser/ui)
app.MapGroup("/api/browser").MapTextBrowserEndpoints();

// Map admin portal endpoints (Admin UI and admin APIs)
app.MapGroup("/admin").MapAdminEndpoints();

// Map voting endpoints used by the admin UI (uses internal /api/voting group)
app.MapTagVotingEndpoints();

// Map batch processing API (extraction background jobs)
app.MapGroup("/api/batch").MapBatchProcessingEndpoints();

// Map Google Drive API endpoints
app.MapGroup("/api/google-drive").MapGoogleDriveEndpoints();

// Map Tag Management API endpoints
app.MapGroup("/api/tags").MapTagManagementEndpoints();

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
// TODO: Move to SearchEndpoints.cs in future refactoring
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
app.MapGet("/debug-raw-text/{documentId:int}", (int documentId) => 
    Results.Redirect($"/api/purchasables/debug/raw-text/{documentId}"));

app.MapGet("/debug-parsing/{documentId:int}", (int documentId) => 
    Results.Redirect($"/api/purchasables/debug/parsing/{documentId}"));

app.MapGet("/debug-parser-service/{documentId:int}", (int documentId) => 
    Results.Redirect($"/api/purchasables/debug/service/{documentId}"));

app.MapGet("/debug-simple-parse/{documentId:int}", (int documentId) => 
    Results.Redirect($"/api/purchasables/debug/simple-parse/{documentId}"));

app.MapGet("/debug-format-analysis/{documentId:int}", (int documentId) => 
    Results.Redirect($"/api/purchasables/debug/format-analysis/{documentId}"));

app.MapGet("/debug-document-text/{documentId:int}", (int documentId) => 
    Results.Redirect($"/api/purchasables/debug/text/{documentId}"));

// Map Razor Pages (for admin login, etc.)
app.MapRazorPages();

// Setup Blazor fallback AFTER all API endpoints
app.MapFallbackToPage("/_Host");


// Run all startup tasks (db, FTS5, drive config, doc count)
await StartupTasks.RunAllAsync(app.Services);

app.Run();

return 0;

// Configuration record for drive setup
public record JumpChainDriveConfig(string name, string folderId);


using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using JumpChainSearch.Data;
using JumpChainSearch.Services;

namespace JumpChainSearch.Extensions;

/// <summary>
/// Legacy test endpoints - DEPRECATED
/// These endpoints are maintained for backward compatibility only.
/// Use the organized endpoint groups in /api/* instead.
/// </summary>
public static class LegacyTestEndpoints
{
    public static WebApplication MapLegacyTestEndpoints(this WebApplication app)
    {
        // Legacy: /test-drive → Use /api/drive/test instead
        app.MapGet("/test-drive", [Obsolete("Use /api/drive/test instead")] 
            async (IGoogleDriveService driveService) =>
        {
            try
            {
                var drives = await driveService.GetAvailableDrivesAsync();
                return Results.Ok(new { 
                    success = true, 
                    message = "Google Drive API connected successfully!", 
                    driveCount = drives.Count(),
                    drives = drives.Take(5) // Show first 5 drives
                });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { 
                    success = false, 
                    error = ex.Message,
                    details = ex.InnerException?.Message
                });
            }
        });

        // Legacy: /test-scan/{folderIndex} → Use /api/drive/scan/{folderIndex} instead
        app.MapGet("/test-scan/{folderIndex:int?}", [Obsolete("Use /api/drive/scan/{folderIndex} instead")]
            async (IGoogleDriveService driveService, int? folderIndex = 0) =>
        {
            try
            {
                // Parse the drive configuration
                var drivesConfig = Environment.GetEnvironmentVariable("JUMPCHAIN_DRIVES_CONFIG");
                if (string.IsNullOrEmpty(drivesConfig))
                {
                    return Results.BadRequest(new { success = false, error = "JUMPCHAIN_DRIVES_CONFIG not found in environment variables" });
                }

                var drives = JsonSerializer.Deserialize<JumpChainDriveConfig[]>(drivesConfig);
                if (drives == null || drives.Length == 0)
                {
                    return Results.BadRequest(new { success = false, error = "No drives configured" });
                }

                var targetFolder = drives[folderIndex!.Value % drives.Length];
                var documents = await driveService.ScanPublicFolderAsync(targetFolder.folderId, targetFolder.name);

                return Results.Ok(new { 
                    success = true, 
                    message = $"Successfully scanned folder: {targetFolder.name}",
                    folderId = targetFolder.folderId,
                    documentCount = documents.Count(),
                    documents = documents.Take(10)
                });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { 
                    success = false, 
                    error = ex.Message,
                    details = ex.InnerException?.Message,
                    stack = ex.StackTrace
                });
            }
        });

        // Legacy: /debug-folder/{folderIndex} → Use /api/drive/debug-folder/{folderIndex} instead
        app.MapGet("/debug-folder/{folderIndex:int?}", [Obsolete("Use /api/drive/debug-folder/{folderIndex} instead")]
            async (int? folderIndex = 0) =>
        {
            try
            {
                // Parse the drive configuration
                var drivesConfig = Environment.GetEnvironmentVariable("JUMPCHAIN_DRIVES_CONFIG");
                if (string.IsNullOrEmpty(drivesConfig))
                {
                    return Results.BadRequest(new { success = false, error = "JUMPCHAIN_DRIVES_CONFIG not found" });
                }

                var drives = JsonSerializer.Deserialize<JumpChainDriveConfig[]>(drivesConfig);
                if (drives == null || drives.Length == 0)
                {
                    return Results.BadRequest(new { success = false, error = "No drives configured" });
                }

                var targetFolder = drives[folderIndex!.Value % drives.Length];
                
                // Try to access the folder directly with Google Drive API
                var serviceAccountKey = Environment.GetEnvironmentVariable("GOOGLE_DRIVE_SERVICE_ACCOUNT_KEY");
                if (string.IsNullOrEmpty(serviceAccountKey))
                {
                    return Results.BadRequest(new { success = false, error = "GOOGLE_DRIVE_SERVICE_ACCOUNT_KEY not found" });
                }

                var credential = Google.Apis.Auth.OAuth2.GoogleCredential.FromJson(serviceAccountKey);
                
                var service = new Google.Apis.Drive.v3.DriveService(new Google.Apis.Services.BaseClientService.Initializer()
                {
                    HttpClientInitializer = credential,
                    ApplicationName = "JumpChain Search Debug",
                });

                // Test 1: Can we get the folder metadata?
                try
                {
                    var folder = await service.Files.Get(targetFolder.folderId).ExecuteAsync();
                    
                    // Test 2: Can we list files in the folder?
                    var listRequest = service.Files.List();
                    listRequest.Q = $"'{targetFolder.folderId}' in parents and trashed=false";
                    listRequest.Fields = "files(id, name, mimeType)";
                    listRequest.PageSize = 10;
                    
                    var response = await listRequest.ExecuteAsync();
                    
                    return Results.Ok(new
                    {
                        success = true,
                        folderInfo = new
                        {
                            id = folder.Id,
                            name = folder.Name,
                            shared = folder.Shared,
                            owners = folder.Owners?.Select(o => o.EmailAddress),
                            permissions = folder.Permissions?.Count ?? 0
                        },
                        filesFound = response.Files?.Count ?? 0,
                        files = response.Files?.Take(5).Select(f => new { f.Id, f.Name, f.MimeType })
                    });
                }
                catch (Exception accessEx)
                {
                    return Results.Ok(new
                    {
                        success = false,
                        error = "Cannot access folder",
                        details = accessEx.Message,
                        suggestion = "Service account may not have permission to access this public folder"
                    });
                }
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { 
                    success = false, 
                    error = ex.Message,
                    details = ex.InnerException?.Message
                });
            }
        });

        // Legacy: /test-all-drives → Use /api/drive/test-all instead
        app.MapGet("/test-all-drives", [Obsolete("Use /api/drive/test-all instead")]
            async (IGoogleDriveService driveService) =>
        {
            try
            {
                var drivesConfig = Environment.GetEnvironmentVariable("JUMPCHAIN_DRIVES_CONFIG");
                if (string.IsNullOrEmpty(drivesConfig))
                {
                    return Results.BadRequest(new { success = false, error = "JUMPCHAIN_DRIVES_CONFIG not found" });
                }

                var drives = JsonSerializer.Deserialize<JumpChainDriveConfig[]>(drivesConfig);
                if (drives == null || drives.Length == 0)
                {
                    return Results.BadRequest(new { success = false, error = "No drives configured" });
                }

                var results = new List<object>();
                for (int i = 0; i < drives.Length; i++)
                {
                    try
                    {
                        var drive = drives[i];
                        var documents = await driveService.ScanPublicFolderAsync(drive.folderId, drive.name);
                        results.Add(new
                        {
                            index = i,
                            name = drive.name,
                            folderId = drive.folderId,
                            success = true,
                            documentCount = documents.Count()
                        });
                    }
                    catch (Exception driveEx)
                    {
                        results.Add(new
                        {
                            index = i,
                            name = drives[i].name,
                            success = false,
                            error = driveEx.Message
                        });
                    }
                }

                return Results.Ok(new
                {
                    success = true,
                    totalDrives = drives.Length,
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

        // Legacy: /test-pdf-parsing → Use /api/text/test-pdf instead
        app.MapGet("/test-pdf-parsing", [Obsolete("Use /api/text/test-pdf instead")]
            () =>
        {
            try
            {
                // This is just to test that PdfPig is working
                return Results.Ok(new {
                    success = true,
                    message = "PDF parsing library is available",
                    pdfPigVersion = typeof(UglyToad.PdfPig.PdfDocument).Assembly.GetName().Version?.ToString(),
                    ready = true
                });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { 
                    success = false, 
                    error = ex.Message,
                    message = "PDF parsing library not available"
                });
            }
        });

        // Legacy: /test-word-parsing → Use /api/text/test-word instead
        app.MapGet("/test-word-parsing", [Obsolete("Use /api/text/test-word instead")]
            () =>
        {
            try
            {
                // This is just to test that DocumentFormat.OpenXml is working
                return Results.Ok(new {
                    success = true,
                    message = "Word document parsing library is available",
                    openXmlVersion = typeof(DocumentFormat.OpenXml.Packaging.WordprocessingDocument).Assembly.GetName().Version?.ToString(),
                    ready = true
                });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { 
                    success = false, 
                    error = ex.Message,
                    message = "Word document parsing library not available"
                });
            }
        });

        // Legacy: /debug-file/{fileId} → Use /api/drive/debug-file/{fileId} instead
        app.MapGet("/debug-file/{fileId}", [Obsolete("Use /api/drive/debug-file/{fileId} instead")]
            async (IGoogleDriveService driveService, string fileId) =>
        {
            try
            {
                var result = await driveService.DebugFilePropertiesAsync(fileId);
                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { 
                    success = false, 
                    error = ex.Message,
                    details = ex.InnerException?.Message
                });
            }
        });

        // Legacy: /debug-auth → Use /api/drive/debug-auth instead
        app.MapGet("/debug-auth", [Obsolete("Use /api/drive/debug-auth instead")]
            (IConfiguration config) =>
        {
            try
            {
                var serviceAccountKey = config["GOOGLE_DRIVE_SERVICE_ACCOUNT_KEY"] ?? Environment.GetEnvironmentVariable("GOOGLE_DRIVE_SERVICE_ACCOUNT_KEY");
                var apiKey = config["GOOGLE_API_KEY"] ?? Environment.GetEnvironmentVariable("GOOGLE_API_KEY");
                
                var hasServiceAccount = !string.IsNullOrEmpty(serviceAccountKey) && serviceAccountKey != "{}";
                var hasApiKey = !string.IsNullOrEmpty(apiKey);
                
                // Try to parse service account key
                string? serviceAccountEmail = null;
                string? projectId = null;
                if (hasServiceAccount && serviceAccountKey != null)
                {
                    try
                    {
                        var keyData = JsonSerializer.Deserialize<Dictionary<string, object>>(serviceAccountKey);
                        if (keyData != null)
                        {
                            serviceAccountEmail = keyData.GetValueOrDefault("client_email")?.ToString();
                            projectId = keyData.GetValueOrDefault("project_id")?.ToString();
                        }
                    }
                    catch { }
                }

                return Results.Ok(new {
                    success = true,
                    authentication = new {
                        hasServiceAccountKey = hasServiceAccount,
                        serviceAccountEmail,
                        projectId,
                        hasApiKey,
                        apiKeyLength = apiKey?.Length ?? 0
                    },
                    environment = Environment.GetEnvironmentVariables()
                        .Cast<System.Collections.DictionaryEntry>()
                        .Where(kv => kv.Key.ToString()?.StartsWith("GOOGLE") == true)
                        .Where(kv => kv.Key != null)
                        .ToDictionary(kv => kv.Key.ToString()!, kv => kv.Value?.ToString()?.Length + " chars")
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

        // Legacy: /start-overnight-processing → Use /api/batch/start-overnight instead
        app.MapPost("/start-overnight-processing", [Obsolete("Use /api/batch/start-overnight instead")]
            (JumpChainDbContext context, IGoogleDriveService driveService) =>
        {
            return Results.Ok(new { message = "Use PowerShell script for overnight processing", timestamp = DateTime.Now });
        });

        // Legacy: /batch-status → Use /api/batch/status instead
        app.MapGet("/batch-status", [Obsolete("Use /api/batch/status instead")]
            async (JumpChainDbContext context) =>
        {
            var stats = await context.JumpDocuments
                .GroupBy(d => d.ExtractedText != null) // Processed = not null (includes empty strings)
                .Select(g => new { HasText = g.Key, Count = g.Count() })
                .ToListAsync();
            
            var processed = stats.FirstOrDefault(s => s.HasText)?.Count ?? 0;
            var unprocessed = stats.FirstOrDefault(s => !s.HasText)?.Count ?? 0;
            var total = processed + unprocessed;
            
            var recentlyProcessed = await context.JumpDocuments
                .Where(d => d.LastScanned > DateTime.Now.AddHours(-1))
                .CountAsync();
            
            // Check if there are more documents to process (null ExtractedText)
            var documentsNeedingProcessing = await context.JumpDocuments
                .CountAsync(d => d.ExtractedText == null);
            
            return Results.Json(new
            {
                total = total,
                processed = processed,
                unprocessed = unprocessed,
                percentComplete = total > 0 ? (double)processed / total * 100 : 0,
                recentlyProcessed = recentlyProcessed,
                hasMoreToProcess = documentsNeedingProcessing > 0,
                lastUpdate = DateTime.Now
            });
        });

        // Legacy: /simple-test → Use /api/database/simple-test instead
        app.MapGet("/simple-test", [Obsolete("Use /api/database/simple-test instead")]
            () => Results.Ok(new { message = "Test endpoint works!", timestamp = DateTime.Now }));

        return app;
    }

    private record JumpChainDriveConfig(string name, string folderId);
}

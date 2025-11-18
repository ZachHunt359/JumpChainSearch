using JumpChainSearch.Data;
using JumpChainSearch.Models;
using JumpChainSearch.Services;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace JumpChainSearch.Extensions;

public static class GoogleDriveEndpoints
{
    public static RouteGroupBuilder MapGoogleDriveEndpoints(this RouteGroupBuilder group)
    {
        // Google Drive API test endpoints
        group.MapGet("/test-drive", TestGoogleDriveConnection);
        group.MapGet("/test-scan/{folderIndex:int?}", TestScanFolder);
        group.MapGet("/test-all-drives", TestAllDrives);
        group.MapGet("/debug-folder/{folderIndex:int?}", DebugFolderAccess);
        group.MapGet("/debug-file/{fileId}", DebugFileProperties); 
        group.MapGet("/debug-auth", DebugAuthentication);
        group.MapGet("/direct-drive-export/{fileId}", DirectDriveExport);
        group.MapGet("/test-ocr/{fileId}", TestOcrExtraction);
        group.MapGet("/test-ocr-direct/{fileId}", TestOcrDirect);
        
        // Drive scanning endpoint for admin portal
        group.MapPost("/scan-all", ScanAllDrives);
        
        // Scan a single drive and save to database
        group.MapPost("/scan-drive/{driveName}", ScanSingleDrive);
        
        // Sync drive configurations from .env
        group.MapPost("/sync-drives", SyncDriveConfigurations);
        
        // Test single drive scan
        group.MapPost("/test-scan-drive/{driveName}", TestScanSingleDrive);
        
        // Discover folder hierarchy with resource keys
        group.MapGet("/discover-folders/{driveName}", DiscoverFolderHierarchy);
        
        // Save discovered folders to FolderConfigurations table
        group.MapPost("/save-folders/{driveName}", SaveDiscoveredFolders);
        
        // Debug endpoint to check drive configuration
        group.MapGet("/debug-drive-config/{driveName}", async (string driveName, JumpChainDbContext dbContext) => {
            var drive = await dbContext.DriveConfigurations
                .FirstOrDefaultAsync(d => d.DriveName == driveName);
            
            if (drive == null)
            {
                return Results.NotFound(new { success = false, error = $"Drive '{driveName}' not found" });
            }
            
            return Results.Ok(new {
                driveName = drive.DriveName,
                driveId = drive.DriveId,
                resourceKey = drive.ResourceKey,
                preferredAuthMethod = drive.PreferredAuthMethod,
                isActive = drive.IsActive,
                parentDriveName = drive.ParentDriveName
            });
        });
        
        // Quick update endpoint for resource keys
        group.MapPost("/update-resource-key/{driveName}", async (string driveName, HttpContext context, JumpChainDbContext dbContext) => {
            var body = await context.Request.ReadFromJsonAsync<ResourceKeyUpdate>();
            if (body == null || string.IsNullOrEmpty(body.resourceKey))
            {
                return Results.BadRequest(new { success = false, error = "resourceKey is required" });
            }
            
            var drive = await dbContext.DriveConfigurations
                .FirstOrDefaultAsync(d => d.DriveName == driveName);
            
            if (drive == null)
            {
                return Results.NotFound(new { success = false, error = $"Drive '{driveName}' not found" });
            }
            
            drive.ResourceKey = body.resourceKey;
            await dbContext.SaveChangesAsync();
            
            return Results.Ok(new {
                success = true,
                driveName = drive.DriveName,
                resourceKey = drive.ResourceKey
            });
        });
        
        // Test endpoint without any dependencies
        group.MapGet("/test-ping", () => {
            Console.WriteLine("TEST PING endpoint hit!");
            return Results.Ok(new { message = "pong", timestamp = DateTime.Now });
        });
        
        // Test endpoint to diagnose GoogleDriveService issues
        group.MapGet("/test-service-creation", (IServiceProvider serviceProvider) => {
            try {
                Console.WriteLine("Attempting to resolve IGoogleDriveService...");
                var service = serviceProvider.GetRequiredService<IGoogleDriveService>();
                Console.WriteLine("✅ Service resolved successfully!");
                return Results.Ok(new { success = true, message = "GoogleDriveService created successfully" });
            }
            catch (Exception ex) {
                Console.WriteLine($"❌ Service resolution failed: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                if (ex.InnerException != null) {
                    Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                    Console.WriteLine($"Inner stack trace: {ex.InnerException.StackTrace}");
                }
                return Results.Problem(
                    detail: ex.ToString(),
                    statusCode: 500,
                    title: "Failed to create GoogleDriveService"
                );
            }
        });

        return group;
    }

    private static async Task<IResult> TestGoogleDriveConnection(IGoogleDriveService driveService)
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
    }

    private static async Task<IResult> TestScanFolder(IGoogleDriveService driveService, int? folderIndex = 0)
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
                folderName = targetFolder.name,
                documentCount = documents.Count(),
                documents = documents.Take(10).Select(d => new { d.Name, d.FolderPath, d.MimeType, d.Size }) // Show first 10 documents
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
    }

    private static async Task<IResult> TestAllDrives(IGoogleDriveService driveService)
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
                    var documentList = documents.ToList();
                    
                    results.Add(new
                    {
                        index = i,
                        name = drive.name,
                        folderId = drive.folderId,
                        accessible = true,
                        documentCount = documentList.Count,
                        sampleDocuments = documentList.Take(3).Select(d => new { d.Name, d.MimeType, d.Size })
                    });
                }
                catch (Exception ex)
                {
                    results.Add(new
                    {
                        index = i,
                        name = drives[i].name,
                        folderId = drives[i].folderId,
                        accessible = false,
                        error = ex.Message,
                        documentCount = 0
                    });
                }
            }

            return Results.Ok(new
            {
                success = true,
                message = "Drive scan completed",
                totalDrives = drives.Length,
                accessibleDrives = results.Count(r => ((dynamic)r).accessible),
                results = results
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
    }

    private static async Task<IResult> DebugFolderAccess(int? folderIndex = 0)
    {
        try
        {
            // Parse the drive configuration
            var drivesConfig = Environment.GetEnvironmentVariable("JUMPCHAIN_DRIVES_CONFIG");
            var drives = JsonSerializer.Deserialize<JumpChainDriveConfig[]>(drivesConfig!);
            var targetFolder = drives![folderIndex!.Value % drives.Length];
            
            // Try to access the folder directly with Google Drive API
            var credential = Google.Apis.Auth.OAuth2.GoogleCredential.FromJson(
                Environment.GetEnvironmentVariable("GOOGLE_DRIVE_SERVICE_ACCOUNT_KEY") ?? "{}");
            
            var service = new Google.Apis.Drive.v3.DriveService(new Google.Apis.Services.BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = "JumpChain Search Debug",
            });

            // Test 1: Can we get the folder metadata?
            try
            {
                var folder = await service.Files.Get(targetFolder.folderId).ExecuteAsync();
                
                return Results.Ok(new
                {
                    success = true,
                    message = "Folder accessible",
                    folder = new
                    {
                        name = folder.Name,
                        id = folder.Id,
                        mimeType = folder.MimeType,
                        parents = folder.Parents,
                        createdTime = folder.CreatedTimeDateTimeOffset,
                        modifiedTime = folder.ModifiedTimeDateTimeOffset
                    }
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
    }

    private static async Task<IResult> DebugFileProperties(IGoogleDriveService driveService, string fileId)
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
    }

    private static Task<IResult> DebugAuthentication(IConfiguration config)
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
            if (hasServiceAccount && !string.IsNullOrEmpty(serviceAccountKey))
            {
                try
                {
                    var keyData = JsonSerializer.Deserialize<JsonElement>(serviceAccountKey);
                    serviceAccountEmail = keyData.GetProperty("client_email").GetString();
                    projectId = keyData.GetProperty("project_id").GetString();
                }
                catch { /* ignore parsing errors */ }
            }

            return Task.FromResult(Results.Ok(new {
                success = true,
                hasServiceAccount,
                hasApiKey,
                serviceAccountEmail,
                projectId,
                serviceAccountKeyLength = serviceAccountKey?.Length ?? 0,
                apiKeyLength = apiKey?.Length ?? 0
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Results.BadRequest(new { 
                success = false, 
                error = ex.Message 
            }));
        }
    }

    private static async Task<IResult> DirectDriveExport(string fileId)
    {
        try
        {
            Console.WriteLine($"Direct Drive API export test for file: {fileId}");
            
            // Use the public API key directly
            var apiKey = Environment.GetEnvironmentVariable("GOOGLE_API_KEY");
            if (string.IsNullOrEmpty(apiKey))
            {
                return Results.BadRequest(new { error = "GOOGLE_API_KEY not configured" });
            }

            var service = new Google.Apis.Drive.v3.DriveService(new Google.Apis.Services.BaseClientService.Initializer()
            {
                ApiKey = apiKey,
                ApplicationName = "JumpChain Search Direct Test",
            });

            // Get file metadata
            var file = await service.Files.Get(fileId).ExecuteAsync();
            Console.WriteLine($"File: {file.Name}, MIME: {file.MimeType}, Size: {file.Size}");

            var result = new
            {
                success = true,
                file = new
                {
                    name = file.Name,
                    id = file.Id,
                    mimeType = file.MimeType,
                    size = file.Size,
                    parents = file.Parents,
                    webViewLink = file.WebViewLink
                }
            };

            // Test different export approaches based on MIME type
            if (file.MimeType == "application/vnd.google-apps.document")
            {
                try
                {
                    // Try to export as plain text
                    var exportRequest = service.Files.Export(fileId, "text/plain");
                    var exportStream = new MemoryStream();
                    await exportRequest.DownloadAsync(exportStream);
                    
                    var textContent = System.Text.Encoding.UTF8.GetString(exportStream.ToArray());
                    
                    return Results.Ok(new
                    {
                        success = true,
                        file = result.file,
                        exportSuccess = true,
                        exportFormat = "text/plain",
                        contentLength = textContent.Length,
                        contentPreview = textContent.Length > 200 ? textContent.Substring(0, 200) + "..." : textContent
                    });
                }
                catch (Exception exportEx)
                {
                    return Results.Ok(new
                    {
                        success = true,
                        file = result.file,
                        exportSuccess = false,
                        exportError = exportEx.Message
                    });
                }
            }
            else if (file.MimeType == "application/pdf")
            {
                try
                {
                    // Try to download PDF directly
                    var downloadRequest = service.Files.Get(fileId);
                    var downloadStream = new MemoryStream();
                    await downloadRequest.DownloadAsync(downloadStream);
                    
                    return Results.Ok(new
                    {
                        success = true,
                        file = result.file,
                        downloadSuccess = true,
                        downloadedSize = downloadStream.Length
                    });
                }
                catch (Exception downloadEx)
                {
                    return Results.Ok(new
                    {
                        success = true,
                        file = result.file,
                        downloadSuccess = false,
                        downloadError = downloadEx.Message
                    });
                }
            }
            
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
    }

    private static async Task<IResult> ScanAllDrives(JumpChainDbContext dbContext, IGoogleDriveService driveService)
    {
        Console.WriteLine("===== ScanAllDrives ENDPOINT INVOKED =====");
        try
        {
            Console.WriteLine("Starting scan - checking database for active drives...");
            
            var drives = await dbContext.DriveConfigurations
                .Where(d => d.IsActive)
                .ToListAsync();

            Console.WriteLine($"Database query complete. Found {drives.Count} active drives");

            if (drives.Count == 0)
            {
                Console.WriteLine("No active drives configured");
                return Results.BadRequest(new { success = false, error = "No active drives configured" });
            }

            var results = new List<object>();
            var totalDocuments = 0;
            var newDocuments = 0;

            foreach (var drive in drives)
            {
                try
                {
                    Console.WriteLine($"Scanning drive: {drive.DriveName} (ID: {drive.DriveId})");
                    
                    // Get existing document count before scan
                    var existingCount = await dbContext.JumpDocuments
                        .Where(d => d.SourceDrive == drive.DriveName)
                        .CountAsync();
                    
                    Console.WriteLine($"Existing documents for {drive.DriveName}: {existingCount}");
                    
                    // Use unified scan method that auto-detects and stores the preferred auth method
                    Console.WriteLine($"Calling ScanDriveUnifiedAsync for {drive.DriveName} (PreferredMethod: {drive.PreferredAuthMethod ?? "Auto-detect"})");
                    var (documents, successfulMethod) = await driveService.ScanDriveUnifiedAsync(drive);
                    var documentsList = documents.ToList();
                    
                    Console.WriteLine($"ScanDriveUnifiedAsync returned {documentsList.Count} documents using {successfulMethod} method");
                    
                    // Update preferred auth method if it worked
                    if (successfulMethod != "None" && drive.PreferredAuthMethod != successfulMethod)
                    {
                        drive.PreferredAuthMethod = successfulMethod;
                        Console.WriteLine($"✅ Updated {drive.DriveName} preferred auth method to: {successfulMethod}");
                    }
                    
                    // Save or update documents in database (optimized batch approach)
                    var newDocsInDrive = 0;
                    var updatedDocs = 0;
                    var driveTagsAdded = 0;
                    
                    if (documentsList.Count > 0)
                    {
                        // Get existing documents with their IDs
                        var fileIds = documentsList.Select(d => d.GoogleDriveFileId).ToList();
                        var existingDocuments = await dbContext.JumpDocuments
                            .Where(d => fileIds.Contains(d.GoogleDriveFileId))
                            .Select(d => new { d.Id, d.GoogleDriveFileId })
                            .ToListAsync();
                        var existingFileIds = existingDocuments.ToDictionary(d => d.GoogleDriveFileId, d => d.Id);
                        
                        Console.WriteLine($"Found {existingFileIds.Count} existing documents - will add drive tags where missing");
                        
                        var addedInThisBatch = new HashSet<string>();
                        
                        foreach (var doc in documentsList)
                        {
                            if (!existingFileIds.ContainsKey(doc.GoogleDriveFileId) && !addedInThisBatch.Contains(doc.GoogleDriveFileId))
                            {
                                // New document - add to database with drive tag already in Tags collection
                                dbContext.JumpDocuments.Add(doc);
                                addedInThisBatch.Add(doc.GoogleDriveFileId);
                                newDocsInDrive++;
                            }
                            else if (existingFileIds.ContainsKey(doc.GoogleDriveFileId))
                            {
                                // Existing document - check if it has a Drive tag for this drive
                                var docId = existingFileIds[doc.GoogleDriveFileId];
                                var hasDriveTag = await dbContext.DocumentTags
                                    .AnyAsync(t => t.JumpDocumentId == docId 
                                                && t.TagCategory == "Drive" 
                                                && t.TagName == drive.DriveName);
                                
                                if (!hasDriveTag)
                                {
                                    // Add drive tag for this location
                                    dbContext.DocumentTags.Add(new DocumentTag
                                    {
                                        JumpDocumentId = docId,
                                        TagCategory = "Drive",
                                        TagName = drive.DriveName
                                    });
                                    driveTagsAdded++;
                                    updatedDocs++;
                                }
                            }
                        }
                        
                        Console.WriteLine($"Added {driveTagsAdded} drive tags to existing documents");
                        
                        // ===== DIAGNOSTIC LOGGING BEFORE SAVE =====
                        Console.WriteLine($"\n=== PRE-SAVE DIAGNOSTIC for {drive.DriveName} ===");
                        Console.WriteLine($"Documents to add: {newDocsInDrive}");
                        
                        // Check for duplicate tags WITHIN each document
                        var docsWithDuplicateTags = 0;
                        foreach (var doc in documentsList.Where(d => !existingFileIds.ContainsKey(d.GoogleDriveFileId) && addedInThisBatch.Contains(d.GoogleDriveFileId)))
                        {
                            var duplicateTags = doc.Tags
                                .GroupBy(t => t.TagName)
                                .Where(g => g.Count() > 1)
                                .ToList();
                            
                            if (duplicateTags.Any())
                            {
                                docsWithDuplicateTags++;
                                Console.WriteLine($"❌ DUPLICATE TAGS FOUND in document: {doc.Name}");
                                foreach (var dup in duplicateTags)
                                {
                                    Console.WriteLine($"  Tag '{dup.Key}' appears {dup.Count()} times");
                                }
                            }
                        }
                        
                        if (docsWithDuplicateTags == 0)
                        {
                            Console.WriteLine("✅ No duplicate tags found within individual documents");
                        }
                        else
                        {
                            Console.WriteLine($"⚠️ Found {docsWithDuplicateTags} documents with duplicate tags");
                        }
                        
                        // Show what EF is tracking
                        var trackedDocs = dbContext.ChangeTracker.Entries<JumpDocument>()
                            .Where(e => e.State == EntityState.Added)
                            .Count();
                        var trackedTags = dbContext.ChangeTracker.Entries<DocumentTag>()
                            .Where(e => e.State == EntityState.Added)
                            .Count();
                        
                        Console.WriteLine($"EF Tracking: {trackedDocs} documents, {trackedTags} tags");
                        Console.WriteLine($"Expected: {newDocsInDrive} documents, ~{newDocsInDrive * 5} tags (avg)");
                        
                        if (trackedDocs != newDocsInDrive)
                        {
                            Console.WriteLine($"⚠️ WARNING: Tracked documents ({trackedDocs}) != Expected ({newDocsInDrive})");
                        }
                        
                        // Sample first few documents and their tags
                        var sampleDocs = dbContext.ChangeTracker.Entries<JumpDocument>()
                            .Where(e => e.State == EntityState.Added)
                            .Take(3)
                            .ToList();
                        
                        Console.WriteLine($"\nSample of first 3 documents being added:");
                        foreach (var entry in sampleDocs)
                        {
                            var doc = entry.Entity;
                            Console.WriteLine($"  - {doc.Name}: {doc.Tags.Count} tags");
                            foreach (var tag in doc.Tags.Take(5))
                            {
                                Console.WriteLine($"    * {tag.TagCategory}: {tag.TagName}");
                            }
                        }
                        
                        Console.WriteLine("=== END DIAGNOSTIC ===\n");
                        // ===== END DIAGNOSTIC LOGGING =====
                        
                        // Save changes after processing all documents for this drive
                        try
                        {
                            await dbContext.SaveChangesAsync();
                            Console.WriteLine($"✅ SAVE SUCCESSFUL: Saved {newDocsInDrive} new documents, added drive tags to {driveTagsAdded} existing documents");
                        }
                        catch (Exception saveEx)
                        {
                            Console.WriteLine($"❌ SAVE FAILED: {saveEx.Message}");
                            
                            // Log first 10 tags that EF tried to insert to identify the pattern
                            var failedTags = dbContext.ChangeTracker.Entries<DocumentTag>()
                                .Where(e => e.State == EntityState.Added)
                                .Take(20)
                                .Select(e => new { 
                                    DocId = e.Entity.JumpDocumentId, 
                                    Tag = e.Entity.TagName,
                                    Category = e.Entity.TagCategory
                                })
                                .ToList();
                            
                            Console.WriteLine("\nFirst 20 tags being inserted:");
                            foreach (var t in failedTags)
                            {
                                Console.WriteLine($"  DocId: {t.DocId}, Category: {t.Category}, Tag: {t.Tag}");
                            }
                            
                            // Show any duplicate combinations
                            var duplicateCombos = failedTags
                                .GroupBy(t => new { t.DocId, t.Tag })
                                .Where(g => g.Count() > 1)
                                .ToList();
                            
                            if (duplicateCombos.Any())
                            {
                                Console.WriteLine($"\n❌ FOUND {duplicateCombos.Count} DUPLICATE (DocId, Tag) COMBINATIONS:");
                                foreach (var combo in duplicateCombos)
                                {
                                    Console.WriteLine($"  DocId: {combo.Key.DocId}, Tag: '{combo.Key.Tag}' appears {combo.Count()} times");
                                }
                            }
                            else
                            {
                                Console.WriteLine("\n⚠️ No obvious duplicate (DocId, Tag) combinations found in sample");
                            }
                            
                            // Clear tracked entities to avoid further issues
                            dbContext.ChangeTracker.Clear();
                            throw;
                        }
                    }
                    
                    // Update the drive configuration
                    drive.LastScanTime = DateTime.Now;
                    
                    // Count documents using the effective drive name (ParentDriveName if set, otherwise DriveName)
                    var effectiveDriveName = drive.ParentDriveName ?? drive.DriveName;
                    var currentCount = await dbContext.JumpDocuments
                        .Where(d => d.SourceDrive == effectiveDriveName)
                        .CountAsync();
                    drive.DocumentCount = currentCount;
                    
                    totalDocuments += currentCount;
                    newDocuments += newDocsInDrive;
                    
                    results.Add(new
                    {
                        drive = drive.DriveName,
                        success = true,
                        totalDocuments = currentCount,
                        newDocuments = newDocsInDrive,
                        driveTagsAdded = driveTagsAdded
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error scanning drive {drive.DriveName}: {ex.Message}");
                    Console.WriteLine($"Stack trace: {ex.StackTrace}");
                    results.Add(new
                    {
                        drive = drive.DriveName,
                        success = false,
                        error = ex.Message,
                        stackTrace = ex.StackTrace
                    });
                }
            }

            Console.WriteLine("Saving changes to database");
            await dbContext.SaveChangesAsync();

            Console.WriteLine("Scan complete!");
            return Results.Ok(new
            {
                success = true,
                message = $"Scanned {drives.Count} drives",
                drivesScanned = drives.Count,
                totalDocuments,
                newDocuments,
                results
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"===== CRITICAL ERROR in ScanAllDrives =====");
            Console.WriteLine($"Exception Type: {ex.GetType().FullName}");
            Console.WriteLine($"Message: {ex.Message}");
            Console.WriteLine($"Stack Trace: {ex.StackTrace}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"Inner Exception: {ex.InnerException.Message}");
                Console.WriteLine($"Inner Stack Trace: {ex.InnerException.StackTrace}");
            }
            Console.WriteLine($"===========================================");
            
            return Results.Json(new
            {
                success = false,
                error = ex.Message,
                detail = ex.ToString(),
                stackTrace = ex.StackTrace
            }, statusCode: 500);
        }
    }

    private static async Task<IResult> ScanSingleDrive(string driveName, JumpChainDbContext dbContext, IGoogleDriveService driveService)
    {
        Console.WriteLine($"===== ScanSingleDrive ENDPOINT INVOKED for {driveName} =====");
        try
        {
            var drive = await dbContext.DriveConfigurations
                .FirstOrDefaultAsync(d => d.DriveName == driveName);
            
            if (drive == null)
            {
                Console.WriteLine($"Drive '{driveName}' not found in configuration");
                return Results.NotFound(new { success = false, error = $"Drive '{driveName}' not found" });
            }
            
            if (!drive.IsActive)
            {
                Console.WriteLine($"Drive '{driveName}' is not active");
                return Results.BadRequest(new { success = false, error = $"Drive '{driveName}' is not active" });
            }
            
            Console.WriteLine($"Scanning drive: {drive.DriveName} (ID: {drive.DriveId})");
            
            // Get existing document count before scan
            var existingCount = await dbContext.JumpDocuments
                .Where(d => d.SourceDrive == drive.DriveName)
                .CountAsync();
            
            Console.WriteLine($"Existing documents for {drive.DriveName}: {existingCount}");
            
            // Use unified scan method
            Console.WriteLine($"Calling ScanDriveUnifiedAsync for {drive.DriveName} (PreferredMethod: {drive.PreferredAuthMethod ?? "Auto-detect"})");
            var (documents, successfulMethod) = await driveService.ScanDriveUnifiedAsync(drive);
            var documentsList = documents.ToList();
            
            Console.WriteLine($"ScanDriveUnifiedAsync returned {documentsList.Count} documents using {successfulMethod} method");
            
            // Update preferred auth method if it worked
            if (successfulMethod != "None" && drive.PreferredAuthMethod != successfulMethod)
            {
                drive.PreferredAuthMethod = successfulMethod;
                Console.WriteLine($"✅ Updated {drive.DriveName} preferred auth method to: {successfulMethod}");
            }
            
            // Save or update documents in database
            var newDocsInDrive = 0;
            var driveTagsAdded = 0;
            
            if (documentsList.Count > 0)
            {
                // Get existing documents with their IDs
                var fileIds = documentsList.Select(d => d.GoogleDriveFileId).ToList();
                var existingDocuments = await dbContext.JumpDocuments
                    .Where(d => fileIds.Contains(d.GoogleDriveFileId))
                    .Select(d => new { d.Id, d.GoogleDriveFileId })
                    .ToListAsync();
                var existingFileIds = existingDocuments.ToDictionary(d => d.GoogleDriveFileId, d => d.Id);
                
                Console.WriteLine($"Found {existingFileIds.Count} existing documents - will add drive tags where missing");
                
                var addedInThisBatch = new HashSet<string>();
                
                foreach (var doc in documentsList)
                {
                    if (!existingFileIds.ContainsKey(doc.GoogleDriveFileId) && !addedInThisBatch.Contains(doc.GoogleDriveFileId))
                    {
                        // New document - add to database
                        dbContext.JumpDocuments.Add(doc);
                        addedInThisBatch.Add(doc.GoogleDriveFileId);
                        newDocsInDrive++;
                    }
                    else if (existingFileIds.ContainsKey(doc.GoogleDriveFileId))
                    {
                        // Existing document - check if it has a Drive tag for this drive
                        var docId = existingFileIds[doc.GoogleDriveFileId];
                        var hasDriveTag = await dbContext.DocumentTags
                            .AnyAsync(t => t.JumpDocumentId == docId 
                                        && t.TagCategory == "Drive" 
                                        && t.TagName == drive.DriveName);
                        
                        if (!hasDriveTag)
                        {
                            // Add drive tag for this location
                            dbContext.DocumentTags.Add(new DocumentTag
                            {
                                JumpDocumentId = docId,
                                TagCategory = "Drive",
                                TagName = drive.DriveName
                            });
                            driveTagsAdded++;
                        }
                    }
                }
                
                Console.WriteLine($"Added {driveTagsAdded} drive tags to existing documents");
                
                // Save changes
                try
                {
                    await dbContext.SaveChangesAsync();
                    Console.WriteLine($"✅ SAVE SUCCESSFUL: Saved {newDocsInDrive} new documents, added drive tags to {driveTagsAdded} existing documents");
                }
                catch (Exception saveEx)
                {
                    Console.WriteLine($"❌ SAVE FAILED: {saveEx.Message}");
                    throw;
                }
            }
            
            // Update drive configuration
            drive.LastScanTime = DateTime.Now;
            
            var currentCount = await dbContext.JumpDocuments
                .Where(d => d.SourceDrive == drive.DriveName)
                .CountAsync();
            drive.DocumentCount = currentCount;
            
            await dbContext.SaveChangesAsync();
            
            Console.WriteLine($"✅ Scan complete for {drive.DriveName}");
            
            return Results.Ok(new
            {
                success = true,
                driveName = drive.DriveName,
                documentsFound = documentsList.Count,
                newDocuments = newDocsInDrive,
                driveTagsAdded = driveTagsAdded,
                totalDocuments = currentCount,
                authMethod = successfulMethod
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"===== ERROR in ScanSingleDrive for {driveName} =====");
            Console.WriteLine($"Message: {ex.Message}");
            Console.WriteLine($"Stack Trace: {ex.StackTrace}");
            
            return Results.Json(new
            {
                success = false,
                driveName = driveName,
                error = ex.Message,
                detail = ex.ToString()
            }, statusCode: 500);
        }
    }

    private static async Task<IResult> TestOcrExtraction(string fileId, IGoogleDriveService driveService, ILogger<Program> logger)
    {
        try
        {
            var startTime = DateTime.Now;
            
            // Log the current directory and tessdata path
            var currentDir = Directory.GetCurrentDirectory();
            var tessdataPath = Path.Combine(currentDir, "tessdata");
            var tessdataExists = Directory.Exists(tessdataPath);
            
            logger.LogInformation($"TEST OCR - Current directory: {currentDir}");
            logger.LogInformation($"TEST OCR - Tessdata path: {tessdataPath}");
            logger.LogInformation($"TEST OCR - Tessdata exists: {tessdataExists}");
            
            var (text, method) = await driveService.ExtractTextWithMethodAsync(fileId);
            var duration = DateTime.Now - startTime;

            // Write detailed log to file for debugging
            var logPath = Path.Combine(currentDir, "ocr-test-log.txt");
            await File.WriteAllTextAsync(logPath, $@"
Test Time: {DateTime.Now}
File ID: {fileId}
Duration: {duration.TotalSeconds}s
Method: {method ?? "NULL"}
Text Length: {text?.Length ?? 0}
Tessdata Exists: {tessdataExists}
Text Preview: {text?.Substring(0, Math.Min(200, text?.Length ?? 0))}
");

            return Results.Ok(new
            {
                success = true,
                fileId,
                extractionMethod = method ?? "NULL",
                textLength = text?.Length ?? 0,
                durationSeconds = duration.TotalSeconds,
                extractedText = text?.Substring(0, Math.Min(500, text?.Length ?? 0)) + "...",
                ocrEnabled = method == "tesseract_ocr",
                currentDirectory = currentDir,
                tessdataPath = tessdataPath,
                tessdataExists = tessdataExists,
                logFilePath = logPath
            });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new
            {
                success = false,
                error = ex.Message,
                stackTrace = ex.StackTrace
            });
        }
    }

    private static async Task<IResult> TestOcrDirect(string fileId, ILogger<Program> logger)
    {
        try
        {
            var startTime = DateTime.Now;
            logger.LogInformation($"=== DIRECT OCR TEST for {fileId} ===");
            
            // Download PDF directly using public API
            var apiKey = Environment.GetEnvironmentVariable("GOOGLE_API_KEY");
            var service = new Google.Apis.Drive.v3.DriveService(new Google.Apis.Services.BaseClientService.Initializer()
            {
                ApiKey = apiKey,
                ApplicationName = "JumpChain OCR Test",
            });

            var request = service.Files.Get(fileId);
            var stream = new MemoryStream();
            await request.DownloadAsync(stream);
            logger.LogInformation($"Downloaded {stream.Length} bytes");
            stream.Position = 0;

            // Try OCR directly
            var tessdataPath = Path.Combine(Directory.GetCurrentDirectory(), "tessdata");
            logger.LogInformation($"Tessdata path: {tessdataPath}, Exists: {Directory.Exists(tessdataPath)}");

            using (var engine = new Tesseract.TesseractEngine(tessdataPath, "eng", Tesseract.EngineMode.Default))
            {
                logger.LogInformation("TesseractEngine created successfully");
                
                using (var images = new ImageMagick.MagickImageCollection())
                {
                    var settings = new ImageMagick.MagickReadSettings { Density = new ImageMagick.Density(300, 300) };
                    images.Read(stream, settings);
                    logger.LogInformation($"PDF converted to {images.Count} images");

                    var textBuilder = new System.Text.StringBuilder();
                    var pageCount = 0;

                    foreach (var image in images.Take(3))  // Just first 3 pages for test
                    {
                        pageCount++;
                        using (var memStream = new MemoryStream())
                        {
                            image.Write(memStream, ImageMagick.MagickFormat.Png);
                            memStream.Position = 0;
                            
                            using (var pix = Tesseract.Pix.LoadFromMemory(memStream.ToArray()))
                            using (var page = engine.Process(pix))
                            {
                                var pageText = page.GetText();
                                logger.LogInformation($"Page {pageCount}: extracted {pageText?.Length ?? 0} chars");
                                if (!string.IsNullOrWhiteSpace(pageText))
                                {
                                    textBuilder.AppendLine(pageText);
                                }
                            }
                        }
                    }

                    var result = textBuilder.ToString();
                    var duration = DateTime.Now - startTime;

                    return Results.Ok(new
                    {
                        success = true,
                        pagesProcessed = pageCount,
                        totalPages = images.Count,
                        textLength = result.Length,
                        durationSeconds = duration.TotalSeconds,
                        preview = result.Length > 500 ? result.Substring(0, 500) + "..." : result
                    });
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Direct OCR test failed");
            return Results.BadRequest(new
            {
                success = false,
                error = ex.Message,
                stackTrace = ex.StackTrace
            });
        }
    }

    private static async Task<IResult> SyncDriveConfigurations(JumpChainDbContext dbContext)
    {
        try
        {
            Console.WriteLine("Syncing drive configurations from .env...");
            
            var drivesConfig = Environment.GetEnvironmentVariable("JUMPCHAIN_DRIVES_CONFIG");
            if (string.IsNullOrEmpty(drivesConfig))
            {
                return Results.BadRequest(new { success = false, error = "JUMPCHAIN_DRIVES_CONFIG environment variable not found" });
            }

            var drives = JsonSerializer.Deserialize<List<JumpChainDriveConfig>>(drivesConfig);
            if (drives == null || drives.Count == 0)
            {
                return Results.BadRequest(new { success = false, error = "No drives found in configuration" });
            }

            var added = 0;
            var updated = 0;
            var unchanged = 0;

            foreach (var drive in drives)
            {
                var existing = await dbContext.DriveConfigurations
                    .FirstOrDefaultAsync(d => d.DriveId == drive.folderId);

                if (existing == null)
                {
                    // Add new drive
                    dbContext.DriveConfigurations.Add(new DriveConfiguration
                    {
                        DriveId = drive.folderId,
                        DriveName = drive.name,
                        ResourceKey = drive.resourceKey,
                        ParentDriveName = drive.parentDriveName,
                        Description = "JumpChain community drive",
                        IsActive = true,
                        LastScanTime = DateTime.MinValue,
                        DocumentCount = 0
                    });
                    added++;
                    Console.WriteLine($"Added: {drive.name}");
                }
                else if (existing.DriveName != drive.name || existing.ResourceKey != drive.resourceKey || existing.ParentDriveName != drive.parentDriveName)
                {
                    // Update name, resource key, or parent drive name if changed
                    existing.DriveName = drive.name;
                    existing.ResourceKey = drive.resourceKey;
                    existing.ParentDriveName = drive.parentDriveName;
                    updated++;
                    Console.WriteLine($"Updated: {drive.name}");
                }
                else
                {
                    unchanged++;
                }
            }

            await dbContext.SaveChangesAsync();
            
            Console.WriteLine($"Sync complete: {added} added, {updated} updated, {unchanged} unchanged");
            
            return Results.Ok(new
            {
                success = true,
                message = "Drive configurations synced successfully",
                added,
                updated,
                unchanged,
                total = drives.Count
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error syncing drive configurations: {ex.Message}");
            return Results.Json(new
            {
                success = false,
                error = ex.Message,
                detail = ex.ToString()
            }, statusCode: 500);
        }
    }

    private static async Task<IResult> TestScanSingleDrive(string driveName, JumpChainDbContext dbContext, IGoogleDriveService driveService)
    {
        try
        {
            Console.WriteLine($"Testing scan of single drive: {driveName}");
            
            var drive = await dbContext.DriveConfigurations
                .FirstOrDefaultAsync(d => d.DriveName == driveName);
            
            if (drive == null)
            {
                return Results.NotFound(new { success = false, error = $"Drive '{driveName}' not found in configuration" });
            }
            
            Console.WriteLine($"Found drive: {drive.DriveName} (ID: {drive.DriveId})");
            Console.WriteLine($"Scanning folder with service account authentication...");
            
            var documents = await driveService.ScanFolderAsync(drive.DriveId, drive.DriveName, drive.ResourceKey, drive.ParentDriveName);
            var documentsList = documents.ToList();
            
            Console.WriteLine($"Scan returned {documentsList.Count} documents");
            
            return Results.Ok(new
            {
                success = true,
                driveName = drive.DriveName,
                driveId = drive.DriveId,
                documentsFound = documentsList.Count,
                sampleDocuments = documentsList.Take(10).Select(d => new { d.Name, d.FolderPath, d.MimeType })
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return Results.Json(new
            {
                success = false,
                error = ex.Message,
                detail = ex.ToString()
            }, statusCode: 500);
        }
    }

    private static async Task<IResult> DiscoverFolderHierarchy(string driveName, JumpChainDbContext dbContext, IGoogleDriveService driveService)
    {
        try
        {
            var drive = await dbContext.DriveConfigurations.FirstOrDefaultAsync(d => d.DriveName == driveName);
            if (drive == null)
            {
                return Results.NotFound(new { success = false, error = $"Drive '{driveName}' not found in configuration" });
            }
            
            Console.WriteLine($"Discovering folder hierarchy for: {drive.DriveName}");
            Console.WriteLine($"Root folder ID: {drive.DriveId}");
            Console.WriteLine($"Root resource key: {drive.ResourceKey ?? "none"}");
            
            var folders = await driveService.DiscoverFolderHierarchyAsync(drive.DriveId, drive.ResourceKey);
            
            Console.WriteLine($"Discovery complete: Found {folders.Count} folders");
            
            return Results.Ok(new
            {
                success = true,
                driveName = drive.DriveName,
                rootFolderId = drive.DriveId,
                rootResourceKey = drive.ResourceKey,
                foldersDiscovered = folders.Count,
                folders = folders.Select(f => new
                {
                    folderId = f.folderId,
                    folderName = f.folderName,
                    resourceKey = f.resourceKey
                })
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return Results.Json(new
            {
                success = false,
                error = ex.Message,
                detail = ex.ToString()
            }, statusCode: 500);
        }
    }
    
    /// <summary>
    /// Save discovered folders to FolderConfigurations table for hierarchical management
    /// </summary>
    private static async Task<IResult> SaveDiscoveredFolders(string driveName, JumpChainDbContext dbContext, IGoogleDriveService driveService)
    {
        try
        {
            var drive = await dbContext.DriveConfigurations
                .FirstOrDefaultAsync(d => d.DriveName == driveName);
                
            if (drive == null)
            {
                return Results.NotFound(new { success = false, error = $"Drive '{driveName}' not found in configuration" });
            }
            
            Console.WriteLine($"===== SAVING FOLDERS FOR {driveName} =====");
            
            // Discover folders
            var discoveredFolders = await driveService.DiscoverFolderHierarchyAsync(drive.DriveId, drive.ResourceKey);
            Console.WriteLine($"Discovered {discoveredFolders.Count} folders");
            
            // Get existing folders for this drive
            var existingFolders = await dbContext.FolderConfigurations
                .Where(f => f.ParentDriveId == drive.Id)
                .ToListAsync();
            var existingFolderIds = existingFolders.Select(f => f.FolderId).ToHashSet();
            
            Console.WriteLine($"Found {existingFolders.Count} existing folder configurations");
            
            int newCount = 0;
            int updatedCount = 0;
            
            foreach (var folder in discoveredFolders)
            {
                if (existingFolderIds.Contains(folder.folderId))
                {
                    // Update existing folder
                    var existing = existingFolders.First(f => f.FolderId == folder.folderId);
                    var changed = false;
                    
                    if (existing.FolderName != folder.folderName)
                    {
                        existing.FolderName = folder.folderName;
                        changed = true;
                    }
                    
                    if (existing.ResourceKey != folder.resourceKey)
                    {
                        existing.ResourceKey = folder.resourceKey;
                        changed = true;
                    }
                    
                    if (changed)
                    {
                        existing.UpdatedAt = DateTime.Now;
                        updatedCount++;
                    }
                }
                else
                {
                    // Create new folder configuration
                    var newFolder = new FolderConfiguration
                    {
                        FolderId = folder.folderId,
                        FolderName = folder.folderName,
                        ParentDriveId = drive.Id,
                        ResourceKey = folder.resourceKey,
                        FolderPath = folder.folderName, // Full path with slashes
                        IsActive = true,
                        IsAutoDiscovered = true,
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now
                    };
                    
                    dbContext.FolderConfigurations.Add(newFolder);
                    newCount++;
                }
            }
            
            await dbContext.SaveChangesAsync();
            
            Console.WriteLine($"✅ Saved: {newCount} new folders, {updatedCount} updated");
            
            return Results.Ok(new
            {
                success = true,
                driveName = driveName,
                foldersDiscovered = discoveredFolders.Count,
                foldersCreated = newCount,
                foldersUpdated = updatedCount,
                totalFolders = newCount + existingFolders.Count
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error saving folders: {ex.Message}");
            return Results.Json(new
            {
                success = false,
                error = ex.Message,
                detail = ex.ToString()
            }, statusCode: 500);
        }
    }
}

public record JumpChainDriveConfig(string name, string folderId, string? resourceKey = null, string? parentDriveName = null);
public record ResourceKeyUpdate(string resourceKey);
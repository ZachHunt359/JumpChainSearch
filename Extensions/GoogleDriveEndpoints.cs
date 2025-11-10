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
        try
        {
            var drives = await dbContext.DriveConfigurations
                .Where(d => d.IsActive)
                .ToListAsync();

            if (drives.Count == 0)
            {
                return Results.BadRequest(new { success = false, error = "No active drives configured" });
            }

            var results = new List<object>();
            var totalDocuments = 0;
            var newDocuments = 0;

            foreach (var drive in drives)
            {
                try
                {
                    Console.WriteLine($"Scanning drive: {drive.DriveName}");
                    
                    // Get existing document count before scan
                    var existingCount = await dbContext.JumpDocuments
                        .Where(d => d.SourceDrive == drive.DriveName)
                        .CountAsync();
                    
                    // Scan the drive using the Google Drive service
                    var documents = await driveService.ScanDriveAsync(drive.DriveId, drive.DriveName);
                    var documentsList = documents.ToList();
                    
                    // Update the drive configuration
                    drive.LastScanTime = DateTime.Now;
                    var currentCount = await dbContext.JumpDocuments
                        .Where(d => d.SourceDrive == drive.DriveName)
                        .CountAsync();
                    drive.DocumentCount = currentCount;
                    
                    var newDocsInDrive = currentCount - existingCount;
                    
                    totalDocuments += currentCount;
                    newDocuments += newDocsInDrive;
                    
                    results.Add(new
                    {
                        drive = drive.DriveName,
                        success = true,
                        totalDocuments = currentCount,
                        newDocuments = newDocsInDrive
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error scanning drive {drive.DriveName}: {ex.Message}");
                    results.Add(new
                    {
                        drive = drive.DriveName,
                        success = false,
                        error = ex.Message
                    });
                }
            }

            await dbContext.SaveChangesAsync();

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
            return Results.BadRequest(new { success = false, error = ex.Message });
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
}

public record JumpChainDriveConfig(string name, string folderId);
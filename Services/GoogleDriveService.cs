using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Drive.v3.Data;
using Google.Apis.Services;
using JumpChainSearch.Models;
using Microsoft.EntityFrameworkCore;
using JumpChainSearch.Data;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;
using System.Text.RegularExpressions;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Tesseract;
using ImageMagick;

namespace JumpChainSearch.Services
{
    public interface IGoogleDriveService
    {
        Task<IEnumerable<JumpDocument>> ScanDriveAsync(string driveId, string driveName);
        Task<IEnumerable<JumpDocument>> ScanFolderAsync(string folderId, string folderName);
        Task<IEnumerable<JumpDocument>> ScanPublicFolderAsync(string folderId, string folderName);
        Task<string?> ExtractTextFromDocumentAsync(string fileId);
        Task<(string? text, string? method)> ExtractTextWithMethodAsync(string fileId);
        Task<IEnumerable<DriveData>> GetAvailableDrivesAsync();
        Task<object> DebugFilePropertiesAsync(string fileId);
    }

    public record DriveData(string Id, string Name, string? Description);

    public class GoogleDriveService : IGoogleDriveService
    {
        private readonly DriveService _driveService;
        private readonly DriveService _publicDriveService;
        private readonly JumpChainDbContext _context;
        private readonly ILogger<GoogleDriveService> _logger;

        public GoogleDriveService(IConfiguration configuration, JumpChainDbContext context, ILogger<GoogleDriveService> logger)
        {
            _context = context;
            _logger = logger;

            try
            {
                // Initialize authenticated Google Drive service (for private/owned content)
                // Try to get credentials from file first, then from environment variable
                GoogleCredential credential;
                var credentialsPath = configuration["GOOGLE_APPLICATION_CREDENTIALS"] 
                                     ?? Environment.GetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS");
                
                _logger.LogInformation($"Credentials path: {credentialsPath ?? "null"}");
                
                if (!string.IsNullOrEmpty(credentialsPath))
                {
                    // Resolve relative paths to absolute
                    if (!Path.IsPathRooted(credentialsPath))
                    {
                        credentialsPath = Path.Combine(Directory.GetCurrentDirectory(), credentialsPath);
                        _logger.LogInformation($"Resolved to absolute path: {credentialsPath}");
                    }
                    
                    if (System.IO.File.Exists(credentialsPath))
                    {
                        // Read from file
                        _logger.LogInformation($"Reading credentials from file: {credentialsPath}");
                        var serviceAccountJson = System.IO.File.ReadAllText(credentialsPath);
                        credential = GoogleCredential.FromJson(serviceAccountJson)
                            .CreateScoped(new[] { 
                                DriveService.Scope.DriveReadonly,
                                DriveService.Scope.Drive
                            });
                        _logger.LogInformation("Successfully loaded credentials from file");
                    }
                    else
                    {
                        _logger.LogWarning($"Credentials file not found at: {credentialsPath}");
                        throw new FileNotFoundException($"Service account credentials file not found at: {credentialsPath}");
                    }
                }
                else
                {
                    // Fall back to JSON string from environment
                    _logger.LogInformation("Credentials file not configured, trying environment variable");
                    var serviceAccountKey = configuration["GOOGLE_DRIVE_SERVICE_ACCOUNT_KEY"] 
                                           ?? Environment.GetEnvironmentVariable("GOOGLE_DRIVE_SERVICE_ACCOUNT_KEY");
                    
                    if (string.IsNullOrEmpty(serviceAccountKey))
                    {
                        throw new InvalidOperationException("No Google Drive credentials configured. Please set GOOGLE_APPLICATION_CREDENTIALS or GOOGLE_DRIVE_SERVICE_ACCOUNT_KEY.");
                    }
                    
                    credential = GoogleCredential.FromJson(serviceAccountKey)
                        .CreateScoped(new[] { 
                            DriveService.Scope.DriveReadonly,
                            DriveService.Scope.Drive
                        });
                    _logger.LogInformation("Successfully loaded credentials from environment");
                }
                
                _driveService = new DriveService(new BaseClientService.Initializer()
                {
                    HttpClientInitializer = credential,
                    ApplicationName = "JumpChain Search",
                });
                _logger.LogInformation("Drive service initialized successfully");

                // Initialize public Google Drive service (for public content using API key)
                var apiKey = configuration["GOOGLE_API_KEY"] 
                            ?? Environment.GetEnvironmentVariable("GOOGLE_API_KEY")
                            ?? "";
                
                _publicDriveService = new DriveService(new BaseClientService.Initializer()
                {
                    ApiKey = apiKey,
                    ApplicationName = "JumpChain Search - Public",
                });
                _logger.LogInformation("Public drive service initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize GoogleDriveService");
                throw;
            }
        }

        public async Task<IEnumerable<DriveData>> GetAvailableDrivesAsync()
        {
            try
            {
                var request = _driveService.Drives.List();
                request.PageSize = 100;
                
                var response = await request.ExecuteAsync();
                
                return response.Drives?.Select(d => new DriveData(d.Id, d.Name, d.BackgroundImageFile?.ToString())) 
                       ?? Enumerable.Empty<DriveData>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching available drives");
                return Enumerable.Empty<DriveData>();
            }
        }

        public async Task<IEnumerable<JumpDocument>> ScanDriveAsync(string driveId, string driveName)
        {
            var documents = new List<JumpDocument>();
            
            try
            {
                var request = _driveService.Files.List();
                request.Q = "trashed=false and (mimeType='application/pdf' or mimeType='application/vnd.google-apps.document' or mimeType='application/vnd.openxmlformats-officedocument.wordprocessingml.document')";
                request.DriveId = driveId;
                request.IncludeItemsFromAllDrives = true;
                request.SupportsAllDrives = true;
                request.Corpora = "drive";
                request.Fields = "nextPageToken, files(id, name, description, mimeType, size, createdTime, modifiedTime, parents, webViewLink, exportLinks, thumbnailLink, hasThumbnail)";
                request.PageSize = 1000;

                string? pageToken = null;
                do
                {
                    request.PageToken = pageToken;
                    var response = await request.ExecuteAsync();

                    if (response.Files != null)
                    {
                        foreach (var file in response.Files)
                        {
                            var document = await ConvertToJumpDocumentAsync(file, driveId, driveName);
                            if (document != null)
                            {
                                documents.Add(document);
                            }
                        }
                    }

                    pageToken = response.NextPageToken;
                } while (pageToken != null);

                _logger.LogInformation($"Scanned {documents.Count} documents from drive {driveName}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error scanning drive {driveName} ({driveId})");
            }

            return documents;
        }

        public async Task<IEnumerable<JumpDocument>> ScanFolderAsync(string folderId, string folderName)
        {
            var documents = new List<JumpDocument>();
            
            try
            {
                await ScanFolderRecursiveAsync(folderId, folderName, documents, $"/{folderName}");
                _logger.LogInformation($"Scanned {documents.Count} documents from folder {folderName}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error scanning folder {folderName} ({folderId})");
            }

            return documents;
        }

        public async Task<IEnumerable<JumpDocument>> ScanPublicFolderAsync(string folderId, string folderName)
        {
            var documents = new List<JumpDocument>();
            
            try
            {
                await ScanPublicFolderRecursiveAsync(folderId, folderName, documents, $"/{folderName}");
                _logger.LogInformation($"Scanned {documents.Count} documents from public folder {folderName}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error scanning public folder {folderName} ({folderId})");
            }

            return documents;
        }

        private async Task ScanPublicFolderRecursiveAsync(string folderId, string rootFolderName, List<JumpDocument> documents, string currentPath)
        {
            try
            {
                var request = _publicDriveService.Files.List();
                request.Q = $"'{folderId}' in parents and trashed=false";
                request.Fields = "nextPageToken, files(id, name, description, mimeType, size, createdTime, modifiedTime, parents, webViewLink, thumbnailLink, hasThumbnail)";
                request.PageSize = 1000;

                string? pageToken = null;
                do
                {
                    request.PageToken = pageToken;
                    var response = await request.ExecuteAsync();

                    if (response.Files != null)
                    {
                        foreach (var file in response.Files)
                        {
                            if (file.MimeType == "application/vnd.google-apps.folder")
                            {
                                // Recursively scan subfolders
                                await ScanPublicFolderRecursiveAsync(file.Id, rootFolderName, documents, $"{currentPath}/{file.Name}");
                            }
                            else if (IsRelevantFileType(file.MimeType))
                            {
                                var document = await ConvertToJumpDocumentAsync(file, folderId, rootFolderName, currentPath);
                                if (document != null)
                                {
                                    documents.Add(document);
                                }
                            }
                        }
                    }

                    pageToken = response.NextPageToken;
                } while (pageToken != null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error scanning public folder {currentPath}: {ex.Message}");
            }
        }

        private async Task ScanFolderRecursiveAsync(string folderId, string rootFolderName, List<JumpDocument> documents, string currentPath)
        {
            try
            {
                var request = _driveService.Files.List();
                request.Q = $"'{folderId}' in parents and trashed=false";
                request.Fields = "nextPageToken, files(id, name, description, mimeType, size, createdTime, modifiedTime, parents, webViewLink, exportLinks, thumbnailLink, hasThumbnail)";
                request.PageSize = 1000;

                string? pageToken = null;
                do
                {
                    request.PageToken = pageToken;
                    var response = await request.ExecuteAsync();

                    if (response.Files != null)
                    {
                        foreach (var file in response.Files)
                        {
                            if (file.MimeType == "application/vnd.google-apps.folder")
                            {
                                // Recursively scan subfolders
                                await ScanFolderRecursiveAsync(file.Id, rootFolderName, documents, $"{currentPath}/{file.Name}");
                            }
                            else if (IsRelevantFileType(file.MimeType))
                            {
                                var document = await ConvertToJumpDocumentAsync(file, folderId, rootFolderName, currentPath);
                                if (document != null)
                                {
                                    documents.Add(document);
                                }
                            }
                        }
                    }

                    pageToken = response.NextPageToken;
                } while (pageToken != null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error scanning folder {currentPath}");
            }
        }

        private static bool IsRelevantFileType(string? mimeType)
        {
            if (string.IsNullOrEmpty(mimeType)) return false;
            
            return mimeType == "application/pdf" ||
                   mimeType == "application/vnd.google-apps.document" ||
                   mimeType == "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
        }

        public async Task<string?> ExtractTextFromDocumentAsync(string fileId)
        {
            // For public documents, try public API key FIRST, then service account as fallback
            var services = new[] { _publicDriveService, _driveService };
            var serviceNames = new[] { "PublicAPI", "ServiceAccount" };
            
            for (int i = 0; i < services.Length; i++)
            {
                var service = services[i];
                var serviceName = serviceNames[i];
                
                try
                {
                    _logger.LogInformation($"Attempting text extraction with {serviceName} for file {fileId}");
                    
                    var file = await service.Files.Get(fileId).ExecuteAsync();
                    
                    if (file.MimeType == "application/vnd.google-apps.document")
                    {
                        // For Google Docs, export as plain text
                        var request = service.Files.Export(fileId, "text/plain");
                        var stream = new MemoryStream();
                        await request.DownloadAsync(stream);
                        stream.Position = 0;
                        
                        using var reader = new StreamReader(stream);
                        var text = await reader.ReadToEndAsync();
                        _logger.LogInformation($"Successfully extracted {text.Length} characters using {serviceName}");
                        return CleanupTextFormatting(text);
                    }
                    else if (file.MimeType == "application/pdf")
                    {
                        // For PDFs, use the improved extraction logic (same as direct-drive-export)
                        try
                        {
                            _logger.LogInformation($"Trying binary PDF extraction with improved PdfPig for file {fileId}");
                            var request = service.Files.Get(fileId);
                            var stream = new MemoryStream();
                            await request.DownloadAsync(stream);
                            stream.Position = 0;
                            
                            // Use the working extraction logic from direct-drive-export endpoint
                            var text = ExtractTextFromPdfImproved(stream);
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                _logger.LogInformation($"Successfully extracted PDF text using improved PdfPig ({text.Length} characters) using {serviceName}");
                                return text;
                            }
                            _logger.LogInformation($"PdfPig returned empty text for {fileId}, trying fallback methods");
                        }
                        catch (Exception pdfPigEx)
                        {
                            _logger.LogInformation($"Improved PdfPig extraction failed, trying Google Drive Export: {pdfPigEx.Message}");
                        }
                        
                        // Fallback: try Google Drive Export (may strip spacing but more reliable for some PDFs)
                        try
                        {
                            _logger.LogInformation($"Trying to export PDF as plain text for file {fileId}");
                            var exportRequest = service.Files.Export(fileId, "text/plain");
                            var exportStream = new MemoryStream();
                            await exportRequest.DownloadAsync(exportStream);
                            exportStream.Position = 0;
                            
                            using var reader = new StreamReader(exportStream);
                            var exportedText = await reader.ReadToEndAsync();
                            if (!string.IsNullOrWhiteSpace(exportedText))
                            {
                                _logger.LogInformation($"Successfully exported PDF as text ({exportedText.Length} characters) using {serviceName} - Note: spacing may be lost");
                                return CleanupTextFormatting(exportedText);
                            }
                            _logger.LogInformation($"Google Drive Export returned empty text for {fileId}, trying OCR");
                        }
                        catch (Exception exportEx)
                        {
                            _logger.LogWarning($"Both PdfPig and Google Drive Export failed for PDF {fileId}: {exportEx.Message}");
                        }
                        
                        // Final fallback: Try OCR
                        try
                        {
                            _logger.LogInformation($"Attempting OCR extraction for PDF {fileId}");
                            var request = service.Files.Get(fileId);
                            var stream = new MemoryStream();
                            await request.DownloadAsync(stream);
                            stream.Position = 0;
                            
                            var ocrText = ExtractTextFromPdfWithOCR(stream);
                            if (!string.IsNullOrWhiteSpace(ocrText))
                            {
                                _logger.LogInformation($"Successfully extracted PDF text using OCR ({ocrText.Length} characters) using {serviceName}");
                                return ocrText;
                            }
                            _logger.LogWarning($"OCR also returned empty text for {fileId}");
                        }
                        catch (Exception ocrEx)
                        {
                            _logger.LogWarning($"OCR extraction also failed for PDF {fileId}: {ocrEx.Message}");
                        }
                        
                        return null;
                    }
                    else if (file.MimeType == "application/vnd.openxmlformats-officedocument.wordprocessingml.document")
                    {
                        // For Word documents, try export as text first (many .docx files on Google Drive are converted to Google Docs)
                        try
                        {
                            _logger.LogInformation($"Trying to export Word document as plain text for file {fileId}");
                            var exportRequest = service.Files.Export(fileId, "text/plain");
                            var exportStream = new MemoryStream();
                            await exportRequest.DownloadAsync(exportStream);
                            exportStream.Position = 0;
                            
                            using var reader = new StreamReader(exportStream);
                            var text = await reader.ReadToEndAsync();
                            if (!string.IsNullOrEmpty(text))
                            {
                                _logger.LogInformation($"Successfully exported Word document as text ({text.Length} characters) using {serviceName}");
                                return CleanupTextFormatting(text);
                            }
                        }
                        catch (Exception exportEx)
                        {
                            _logger.LogInformation($"Export as text failed, trying direct download: {exportEx.Message}");
                        }
                        
                        // Fallback: try to download as actual Word document and parse
                        try
                        {
                            var request = service.Files.Get(fileId);
                            var stream = new MemoryStream();
                            await request.DownloadAsync(stream);
                            stream.Position = 0;
                            
                            var text = ExtractTextFromWordDocument(stream);
                            if (!string.IsNullOrEmpty(text))
                            {
                                _logger.LogInformation($"Successfully extracted Word document text ({text.Length} characters) using {serviceName}");
                            }
                            return text;
                        }
                        catch (Exception downloadEx)
                        {
                            _logger.LogWarning($"Both export and download failed for Word document {fileId}: {downloadEx.Message}");
                            return null;
                        }
                    }
                    else if (file.MimeType == "text/plain")
                    {
                        // For plain text files
                        var request = service.Files.Get(fileId);
                        var stream = new MemoryStream();
                        await request.DownloadAsync(stream);
                        stream.Position = 0;
                        
                        using var reader = new StreamReader(stream);
                        var text = await reader.ReadToEndAsync();
                        _logger.LogInformation($"Successfully extracted plain text ({text.Length} characters) using {serviceName}");
                        return CleanupTextFormatting(text);
                    }
                    
                    _logger.LogWarning($"Text extraction not implemented for MIME type: {file.MimeType}");
                    return null;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Failed to extract text using {serviceName}: {ex.Message}");
                    
                    // If this is the last service to try, log the error
                    if (i == services.Length - 1)
                    {
                        _logger.LogError(ex, $"All authentication methods failed for document {fileId}");
                    }
                    
                    // Continue to try the next service
                    continue;
                }
            }
            
            return null;
        }

        public async Task<(string? text, string? method)> ExtractTextWithMethodAsync(string fileId)
        {
            // For public documents, try public API key FIRST, then service account as fallback
            var services = new[] { _publicDriveService, _driveService };
            var serviceNames = new[] { "PublicAPI", "ServiceAccount" };
            
            for (int i = 0; i < services.Length; i++)
            {
                var service = services[i];
                var serviceName = serviceNames[i];
                
                try
                {
                    _logger.LogInformation($"Attempting text extraction with {serviceName} for file {fileId}");
                    
                    var file = await service.Files.Get(fileId).ExecuteAsync();
                    
                    if (file.MimeType == "application/vnd.google-apps.document")
                    {
                        // For Google Docs, export as plain text
                        var request = service.Files.Export(fileId, "text/plain");
                        var stream = new MemoryStream();
                        await request.DownloadAsync(stream);
                        stream.Position = 0;
                        
                        using var reader = new StreamReader(stream);
                        var text = await reader.ReadToEndAsync();
                        _logger.LogInformation($"Successfully extracted {text.Length} characters using {serviceName}");
                        return (CleanupTextFormatting(text), "google_drive_export");
                    }
                    else if (file.MimeType == "application/pdf")
                    {
                        // For PDFs, use improved PdfPig extraction first
                        try
                        {
                            _logger.LogInformation($"Trying improved PdfPig extraction for file {fileId}");
                            var request = service.Files.Get(fileId);
                            var stream = new MemoryStream();
                            await request.DownloadAsync(stream);
                            stream.Position = 0;
                            
                            var (text, method) = ExtractTextFromPdfWithMethod(stream);
                            _logger.LogInformation($"PdfPig extraction result: text={text?.Length ?? 0} chars, method={method}");
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                _logger.LogInformation($"Successfully extracted PDF text using {method} ({text.Length} characters) using {serviceName}");
                                return (text, "improved_pdfpig");
                            }
                            _logger.LogInformation($"PdfPig returned empty/null text for {fileId}, trying fallback methods");
                        }
                        catch (Exception pdfPigEx)
                        {
                            _logger.LogInformation($"Improved PdfPig extraction failed, trying Google Drive Export: {pdfPigEx.Message}");
                        }
                        
                        // Fallback: try Google Drive Export 
                        try
                        {
                            _logger.LogInformation($"Trying to export PDF as plain text for file {fileId}");
                            var exportRequest = service.Files.Export(fileId, "text/plain");
                            var exportStream = new MemoryStream();
                            await exportRequest.DownloadAsync(exportStream);
                            exportStream.Position = 0;
                            
                            using var reader = new StreamReader(exportStream);
                            var exportedText = await reader.ReadToEndAsync();
                            if (!string.IsNullOrWhiteSpace(exportedText))
                            {
                                _logger.LogInformation($"Successfully exported PDF as text ({exportedText.Length} characters) using {serviceName}");
                                return (CleanupTextFormatting(exportedText), "google_drive_export");
                            }
                            _logger.LogInformation($"Google Drive Export returned empty text for {fileId}, trying OCR");
                        }
                        catch (Exception exportEx)
                        {
                            _logger.LogWarning($"Both improved PdfPig and Google Drive Export failed for PDF {fileId}: {exportEx.Message}");
                        }
                        
                        // Final fallback: Try OCR
                        try
                        {
                            _logger.LogInformation($"=== Attempting OCR extraction for PDF {fileId} ===");
                            System.IO.File.AppendAllText("ocr-debug.txt", $"[{DateTime.Now}] Starting OCR for {fileId} using {serviceName}\n");
                            
                            var request = service.Files.Get(fileId);
                            var stream = new MemoryStream();
                            await request.DownloadAsync(stream);
                            _logger.LogInformation($"Downloaded PDF for OCR, stream length: {stream.Length} bytes");
                            System.IO.File.AppendAllText("ocr-debug.txt", $"[{DateTime.Now}] Downloaded {stream.Length} bytes\n");
                            stream.Position = 0;
                            
                            var (ocrText, quality) = ExtractTextFromPdfWithOCRAndQuality(stream);
                            _logger.LogInformation($"OCR extraction completed, result: {ocrText?.Length ?? 0} characters, quality: {quality:F2}");
                            System.IO.File.AppendAllText("ocr-debug.txt", $"[{DateTime.Now}] OCR returned {ocrText?.Length ?? 0} chars, quality: {quality:F2}\n");
                            
                            if (!string.IsNullOrWhiteSpace(ocrText))
                            {
                                var methodName = quality < 0.5 
                                    ? $"tesseract_ocr_low_quality_{quality:F2}" 
                                    : $"tesseract_ocr_{quality:F2}";
                                _logger.LogInformation($"Successfully extracted PDF text using OCR ({ocrText.Length} characters, quality: {quality:F2}) using {serviceName}");
                                return (ocrText, methodName);
                            }
                            _logger.LogWarning($"OCR also returned empty text for {fileId}");
                            System.IO.File.AppendAllText("ocr-debug.txt", $"[{DateTime.Now}] OCR returned empty/null\n");
                        }
                        catch (Exception ocrEx)
                        {
                            _logger.LogError(ocrEx, $"OCR extraction also failed for PDF {fileId}");
                            System.IO.File.AppendAllText("ocr-debug.txt", $"[{DateTime.Now}] OCR EXCEPTION: {ocrEx.Message}\nStack: {ocrEx.StackTrace}\n");
                        }
                        
                        // Try next service if available
                        continue;
                    }
                    else if (file.MimeType == "application/vnd.openxmlformats-officedocument.wordprocessingml.document")
                    {
                        // For Word documents, try export as text first
                        try
                        {
                            _logger.LogInformation($"Trying to export Word document as plain text for file {fileId}");
                            var exportRequest = service.Files.Export(fileId, "text/plain");
                            var exportStream = new MemoryStream();
                            await exportRequest.DownloadAsync(exportStream);
                            exportStream.Position = 0;
                            
                            using var reader = new StreamReader(exportStream);
                            var text = await reader.ReadToEndAsync();
                            if (!string.IsNullOrEmpty(text))
                            {
                                _logger.LogInformation($"Successfully exported Word document as text ({text.Length} characters) using {serviceName}");
                                return (CleanupTextFormatting(text), "google_drive_export");
                            }
                        }
                        catch (Exception exportEx)
                        {
                            _logger.LogInformation($"Export as text failed, trying direct download: {exportEx.Message}");
                        }
                        
                        // Fallback: try to download as actual Word document and parse
                        try
                        {
                            var request = service.Files.Get(fileId);
                            var stream = new MemoryStream();
                            await request.DownloadAsync(stream);
                            stream.Position = 0;
                            
                            var text = ExtractTextFromWordDocument(stream);
                            if (!string.IsNullOrEmpty(text))
                            {
                                _logger.LogInformation($"Successfully extracted Word document text ({text.Length} characters) using {serviceName}");
                                return (text, "word_document_parsing");
                            }
                        }
                        catch (Exception downloadEx)
                        {
                            _logger.LogWarning($"Both export and download failed for Word document {fileId}: {downloadEx.Message}");
                        }
                        
                        // Try next service if available
                        continue;
                    }
                    else if (file.MimeType == "text/plain")
                    {
                        // For plain text files
                        var request = service.Files.Get(fileId);
                        var stream = new MemoryStream();
                        await request.DownloadAsync(stream);
                        stream.Position = 0;
                        
                        using var reader = new StreamReader(stream);
                        var text = await reader.ReadToEndAsync();
                        _logger.LogInformation($"Successfully extracted plain text ({text.Length} characters) using {serviceName}");
                        return (CleanupTextFormatting(text), "plain_text");
                    }
                    
                    _logger.LogWarning($"Text extraction not implemented for MIME type: {file.MimeType}");
                    // Try next service if available
                    continue;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Failed to extract text using {serviceName}: {ex.Message}");
                    
                    // If this is the last service to try, log the error
                    if (i == services.Length - 1)
                    {
                        _logger.LogError(ex, $"All authentication methods failed for document {fileId}");
                    }
                    
                    // Continue to try the next service
                    continue;
                }
            }
            
            return (null, null);
        }

        private (string? text, string method) ExtractTextFromPdfWithMethod(Stream pdfStream)
        {
            try
            {
                using var document = PdfDocument.Open(pdfStream);
                var allText = new List<string>();
                var pageCount = 0;
                var successfulExtractionMethod = "BasicPageText";

                foreach (var page in document.GetPages())
                {
                    pageCount++;
                    string? pageText = null;
                    #pragma warning disable CS0219 // Variable is assigned but its value is never used
                    string extractionMethod = "BasicPageText";
                    #pragma warning restore CS0219

                    try
                    {
                        // First, try ContentOrderTextExtractor (best for preserving spaces and reading order)
                        try
                        {
                            pageText = ContentOrderTextExtractor.GetText(page);
                            extractionMethod = "ContentOrderTextExtractor";
                            _logger.LogInformation($"Page {pageCount}: ContentOrderTextExtractor extracted {pageText?.Length} characters. Sample: '{pageText?.Substring(0, Math.Min(50, pageText?.Length ?? 0))}'");
                            
                            if (!string.IsNullOrWhiteSpace(pageText))
                            {
                                successfulExtractionMethod = "ContentOrderTextExtractor";
                            }
                        }
                        catch (Exception contentOrderEx)
                        {
                            _logger.LogInformation(contentOrderEx, $"ContentOrderTextExtractor failed for page {pageCount}, trying word-based extraction");
                            
                            // Fallback: try NearestNeighbourWordExtractor with manual word joining
                            try
                            {
                                var words = page.GetWords(NearestNeighbourWordExtractor.Instance);
                                if (words.Any())
                                {
                                    pageText = string.Join(" ", words.Select(w => w.Text));
                                    extractionMethod = "NearestNeighbourWordExtractor";
                                    _logger.LogInformation($"Page {pageCount}: Word-based extraction extracted {pageText?.Length} characters. Sample: '{pageText?.Substring(0, Math.Min(50, pageText?.Length ?? 0))}'");
                                    
                                    if (!string.IsNullOrWhiteSpace(pageText))
                                    {
                                        successfulExtractionMethod = "NearestNeighbourWordExtractor";
                                    }
                                }
                            }
                            catch (Exception wordEx)
                            {
                                _logger.LogInformation(wordEx, $"Word-based extraction failed for page {pageCount}, using basic page.Text");
                                
                                pageText = page.Text;
                                extractionMethod = "BasicPageText";
                                _logger.LogInformation($"Page {pageCount}: Basic page.Text extracted {pageText?.Length} characters. Sample: '{pageText?.Substring(0, Math.Min(50, pageText?.Length ?? 0))}'");
                            }
                        }

                        if (!string.IsNullOrEmpty(pageText))
                        {
                            allText.Add(pageText);
                        }
                        else
                        {
                            _logger.LogWarning($"No text found on PDF page {pageCount}");
                        }
                    }
                    catch (Exception pageEx)
                    {
                        _logger.LogWarning(pageEx, $"Error extracting text from PDF page {pageCount}, skipping page");
                        continue;
                    }
                }

                var finalText = string.Join("\n\n", allText.Where(t => !string.IsNullOrWhiteSpace(t)));
                
                if (string.IsNullOrEmpty(finalText))
                {
                    _logger.LogWarning("PDF extraction completed but no text was found");
                    return (null, "none");
                }

                var totalChars = finalText.Length;
                _logger.LogInformation($"PDF extraction complete: {totalChars} total characters from {pageCount} pages using {successfulExtractionMethod}");
                
                return (finalText, successfulExtractionMethod);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error extracting text from PDF");
                return (null, "error");
            }
        }

        private (string? text, double quality) ExtractTextFromPdfWithOCRAndQuality(Stream pdfStream, int maxPages = 50, int timeoutMinutes = 5)
        {
            _logger.LogInformation($"=== Starting OCR extraction with {timeoutMinutes} minute timeout ===");
            try
            {
                // Run OCR with timeout to prevent hanging
                var ocrTask = Task.Run(() => ExtractTextFromPdfWithOCRInternal(pdfStream, maxPages));
                if (ocrTask.Wait(TimeSpan.FromMinutes(timeoutMinutes)))
                {
                    var text = ocrTask.Result;
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        var quality = CalculateOcrQuality(text);
                        return (text, quality);
                    }
                    return (null, 0.0);
                }
                else
                {
                    _logger.LogWarning($"OCR extraction timed out after {timeoutMinutes} minutes");
                    return (null, 0.0);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in OCR extraction with timeout");
                return (null, 0.0);
            }
        }

        private string? ExtractTextFromPdfWithOCR(Stream pdfStream, int maxPages = 50, int timeoutMinutes = 5)
        {
            var (text, _) = ExtractTextFromPdfWithOCRAndQuality(pdfStream, maxPages, timeoutMinutes);
            return text;
        }

        private string? ExtractTextFromPdfWithOCRInternal(Stream pdfStream, int maxPages = 50)
        {
            _logger.LogInformation("=== Starting internal OCR extraction ===");
            System.IO.File.AppendAllText("ocr-debug.txt", $"[{DateTime.Now}] Inside ExtractTextFromPdfWithOCR\n");
            try
            {
                pdfStream.Position = 0;
                var textBuilder = new StringBuilder();
                
                // Check if tessdata directory exists
                var tessdataPath = Path.Combine(Directory.GetCurrentDirectory(), "tessdata");
                _logger.LogInformation($"Current directory: {Directory.GetCurrentDirectory()}");
                _logger.LogInformation($"Checking for tessdata at: {tessdataPath}");
                System.IO.File.AppendAllText("ocr-debug.txt", $"[{DateTime.Now}] Checking tessdata at: {tessdataPath}\n");
                
                if (!Directory.Exists(tessdataPath))
                {
                    _logger.LogWarning("Tessdata directory not found at {Path}. OCR extraction skipped.", tessdataPath);
                    System.IO.File.AppendAllText("ocr-debug.txt", $"[{DateTime.Now}] Tessdata directory NOT FOUND\n");
                    return null;
                }
                
                System.IO.File.AppendAllText("ocr-debug.txt", $"[{DateTime.Now}] Tessdata directory EXISTS\n");
                
                _logger.LogInformation("Tessdata directory found, initializing Tesseract engine");
                System.IO.File.AppendAllText("ocr-debug.txt", $"[{DateTime.Now}] Initializing TesseractEngine\n");

                using (var engine = new TesseractEngine(tessdataPath, "eng", EngineMode.Default))
                {
                    _logger.LogInformation("Tesseract engine initialized successfully");
                    System.IO.File.AppendAllText("ocr-debug.txt", $"[{DateTime.Now}] TesseractEngine initialized OK\n");
                    
                    // Convert PDF to images using ImageMagick
                    // Configure Ghostscript path explicitly
                    MagickNET.SetGhostscriptDirectory(@"C:\Program Files\gs\gs10.03.1\bin");
                    
                    _logger.LogInformation("Creating MagickImageCollection for PDF");
                    var images = new MagickImageCollection();
                    var settings = new MagickReadSettings
                    {
                        Density = new Density(300, 300) // 300 DPI for good OCR quality
                        // Don't set Format - let ImageMagick detect it's a PDF
                    };

                    _logger.LogInformation("Reading PDF stream with ImageMagick");
                    System.IO.File.AppendAllText("ocr-debug.txt", $"[{DateTime.Now}] About to read PDF with ImageMagick\n");
                    
                    try
                    {
                        images.Read(pdfStream, settings);
                        _logger.LogInformation($"Successfully loaded {images.Count} pages from PDF");
                        System.IO.File.AppendAllText("ocr-debug.txt", $"[{DateTime.Now}] ImageMagick loaded {images.Count} pages\n");
                    }
                    catch (Exception imgEx)
                    {
                        System.IO.File.AppendAllText("ocr-debug.txt", $"[{DateTime.Now}] ImageMagick Read FAILED: {imgEx.Message}\n");
                        _logger.LogError(imgEx, "ImageMagick failed to read PDF");
                        return null;
                    }
                    
                    int pageNum = 0;
                    foreach (var image in images.Take(maxPages))
                    {
                        pageNum++;
                        try
                        {
                            // Convert image to format suitable for Tesseract
                            using (var memStream = new MemoryStream())
                            {
                                image.Write(memStream, MagickFormat.Png);
                                memStream.Position = 0;
                                
                                using (var pix = Pix.LoadFromMemory(memStream.ToArray()))
                                {
                                    using (var page = engine.Process(pix))
                                    {
                                        var pageText = page.GetText();
                                        if (!string.IsNullOrWhiteSpace(pageText))
                                        {
                                            textBuilder.AppendLine(pageText);
                                            _logger.LogInformation($"OCR extracted {pageText.Length} chars from page {pageNum}");
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception pageEx)
                        {
                            _logger.LogWarning($"OCR failed on page {pageNum}: {pageEx.Message}");
                        }
                    }
                    
                    if (images.Count > maxPages)
                    {
                        _logger.LogInformation($"OCR stopped at {maxPages} pages (total {images.Count} pages)");
                    }
                }

                var result = textBuilder.ToString();
                if (string.IsNullOrWhiteSpace(result))
                {
                    _logger.LogInformation("OCR completed but no text found");
                    return null;
                }

                // Calculate quality before cleaning (more accurate on raw OCR output)
                var quality = CalculateOcrQuality(result);
                _logger.LogInformation($"OCR extraction successful: {result.Length} characters, quality: {quality:F2}");
                
                if (quality < 0.3)
                {
                    _logger.LogWarning($"OCR quality very low ({quality:F2}) - text may be garbled");
                }
                
                return CleanupTextFormatting(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in OCR extraction");
                return null;
            }
        }

        private double CalculateOcrQuality(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0.0;

            var words = text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
                return 0.0;

            // Calculate metrics
            var totalChars = text.Length;
            var alphaChars = text.Count(char.IsLetter);
            var alphaRatio = (double)alphaChars / totalChars;
            
            // Average word length (garbled text has many short fragments)
            var avgWordLength = words.Average(w => w.Length);
            var wordLengthScore = Math.Min(avgWordLength / 5.0, 1.0); // Normalize to 0-1, ideal ~5 chars
            
            // Single character words ratio (high ratio indicates problems)
            var singleCharWords = words.Count(w => w.Length == 1);
            var singleCharRatio = (double)singleCharWords / words.Length;
            var singleCharScore = 1.0 - Math.Min(singleCharRatio * 2, 1.0); // Penalize high ratios
            
            // Very short words (1-2 chars) - some are OK (a, I, is, to) but too many indicates garbling
            var veryShortWords = words.Count(w => w.Length <= 2);
            var veryShortRatio = (double)veryShortWords / words.Length;
            var shortWordScore = 1.0 - Math.Min(veryShortRatio * 1.5, 1.0);
            
            // Combine scores (weighted average)
            var qualityScore = (
                alphaRatio * 0.3 +           // 30% weight on alphabetic content
                wordLengthScore * 0.25 +     // 25% weight on word length
                singleCharScore * 0.25 +     // 25% weight on single char ratio
                shortWordScore * 0.20        // 20% weight on very short words
            );
            
            return qualityScore;
        }

        private string? ExtractTextFromPdfImproved(Stream pdfStream)
        {
            try
            {
                pdfStream.Position = 0;
                
                if (pdfStream.Length == 0)
                {
                    _logger.LogWarning("PDF stream is empty");
                    return null;
                }

                using var document = PdfDocument.Open(pdfStream, new ParsingOptions 
                { 
                    UseLenientParsing = true,
                    SkipMissingFonts = true
                });
                
                var textBuilder = new StringBuilder();
                int pageNum = 1;
                
                foreach (var page in document.GetPages())
                {
                    try
                    {
                        string? pageText = null;
                        string extractionMethod = "unknown";
                        
                        // Method 1: Try ContentOrderTextExtractor first
                        try 
                        {
                            pageText = ContentOrderTextExtractor.GetText(page);
                            extractionMethod = "ContentOrderTextExtractor";
                            _logger.LogInformation($"Page {pageNum}: ContentOrderTextExtractor extracted {pageText?.Length ?? 0} characters");
                        }
                        catch (Exception contentEx)
                        {
                            _logger.LogInformation($"ContentOrderTextExtractor failed for page {pageNum}: {contentEx.Message}, trying word-based extraction");
                            
                            // Method 2: Fallback to word-based extraction with proper spacing
                            try 
                            {
                                var words = page.GetWords(NearestNeighbourWordExtractor.Instance);
                                var wordTexts = new List<string>();
                                foreach (var word in words)
                                {
                                    if (!string.IsNullOrWhiteSpace(word.Text))
                                    {
                                        wordTexts.Add(word.Text);
                                    }
                                }
                                pageText = string.Join(" ", wordTexts);
                                extractionMethod = "NearestNeighbourWordExtractor";
                                _logger.LogInformation($"Page {pageNum}: Word-based extraction extracted {pageText?.Length ?? 0} characters");
                            }
                            catch (Exception wordEx)
                            {
                                _logger.LogInformation($"Word-based extraction failed for page {pageNum}: {wordEx.Message}, using basic page.Text");
                                // Method 3: Final fallback to basic page.Text
                                pageText = page.Text;
                                extractionMethod = "BasicPageText";
                                _logger.LogInformation($"Page {pageNum}: Basic page.Text extracted {pageText?.Length ?? 0} characters");
                            }
                        }
                        
                        if (!string.IsNullOrWhiteSpace(pageText))
                        {
                            textBuilder.AppendLine(pageText);
                        }
                        _logger.LogInformation($"Page {pageNum}: Final extraction using {extractionMethod}");
                        pageNum++;
                        
                        // Limit extraction to avoid memory issues
                        if (pageNum > 100)
                        {
                            _logger.LogInformation($"PDF extraction stopped at {pageNum - 1} pages to avoid memory issues");
                            break;
                        }
                    }
                    catch (Exception pageEx)
                    {
                        _logger.LogWarning($"Failed to extract text from page {pageNum}: {pageEx.Message}");
                        pageNum++;
                    }
                }
                
                var pdfText = textBuilder.ToString();
                _logger.LogInformation($"PdfPig extraction completed! Total length: {pdfText.Length}");
                
                return string.IsNullOrWhiteSpace(pdfText) ? "" : CleanupTextFormatting(pdfText);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in improved PDF extraction");
                return "";
            }
        }

        private string? ExtractTextFromPdf(Stream pdfStream)
        {
            try
            {
                // Reset stream position to beginning
                pdfStream.Position = 0;
                
                // Check if stream has valid content
                if (pdfStream.Length == 0)
                {
                    _logger.LogWarning("PDF stream is empty");
                    return null;
                }

                using var document = PdfDocument.Open(pdfStream, new ParsingOptions 
                { 
                    UseLenientParsing = true,
                    SkipMissingFonts = true
                });
                
                var textBuilder = new System.Text.StringBuilder();
                int pageCount = 0;
                
                foreach (var page in document.GetPages())
                {
                    try
                    {
                        // Method 1: Try ContentOrderTextExtractor (recommended by PdfPig docs)
                        string? pageText = null;
                        string extractionMethod = "unknown";
                        try 
                        {
                            pageText = ContentOrderTextExtractor.GetText(page);
                            extractionMethod = "ContentOrderTextExtractor";
                            _logger.LogInformation($"Page {pageCount + 1}: ContentOrderTextExtractor extracted {pageText?.Length} characters. Sample: '{pageText?.Substring(0, Math.Min(50, pageText?.Length ?? 0))}'");
                        }
                        catch (Exception contentEx)
                        {
                            _logger.LogInformation(contentEx, $"ContentOrderTextExtractor failed for page {pageCount + 1}, trying word-based extraction");
                            
                            // Method 2: Fallback to word-based extraction with proper spacing
                            try 
                            {
                                var words = page.GetWords(NearestNeighbourWordExtractor.Instance);
                                var wordTexts = new List<string>();
                                foreach (var word in words)
                                {
                                    if (!string.IsNullOrWhiteSpace(word.Text))
                                    {
                                        wordTexts.Add(word.Text);
                                    }
                                }
                                pageText = string.Join(" ", wordTexts);
                                extractionMethod = "NearestNeighbourWordExtractor";
                                _logger.LogInformation($"Page {pageCount + 1}: Word-based extraction extracted {pageText?.Length} characters. Sample: '{pageText?.Substring(0, Math.Min(50, pageText?.Length ?? 0))}'");
                            }
                            catch (Exception wordEx)
                            {
                                _logger.LogInformation(wordEx, $"Word-based extraction failed for page {pageCount + 1}, using basic page.Text");
                                // Method 3: Final fallback to basic page.Text (preserves original behavior)
                                pageText = page.Text;
                                extractionMethod = "BasicPageText";
                                _logger.LogInformation($"Page {pageCount + 1}: Basic page.Text extracted {pageText?.Length} characters. Sample: '{pageText?.Substring(0, Math.Min(50, pageText?.Length ?? 0))}'");
                            }
                        }
                        
                        if (!string.IsNullOrWhiteSpace(pageText))
                        {
                            textBuilder.AppendLine(pageText);
                        }
                        _logger.LogDebug($"Page {pageCount + 1} processed using {extractionMethod} method");
                        pageCount++;
                        
                        // Limit extraction to avoid memory issues with very large documents
                        if (pageCount >= 100)
                        {
                            _logger.LogInformation($"PDF extraction stopped at {pageCount} pages to avoid memory issues");
                            break;
                        }
                    }
                    catch (Exception pageEx)
                    {
                        _logger.LogWarning(pageEx, $"Error extracting text from PDF page {pageCount + 1}, skipping page");
                        // Continue to next page instead of failing entirely
                        continue;
                    }
                }
                
                var rawText = textBuilder.ToString();
                
                if (string.IsNullOrWhiteSpace(rawText))
                {
                    _logger.LogWarning("PDF extraction completed but no text was found");
                    return ""; // Return empty string instead of null to indicate processing was attempted
                }
                
                return CleanupTextFormatting(rawText);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                _logger.LogWarning(ex, "PDF appears to be corrupted or malformed (ArgumentOutOfRangeException)");
                return ""; // Return empty string to indicate extraction was attempted but failed
            }
            catch (System.IO.IOException ex)
            {
                _logger.LogWarning(ex, "IO error reading PDF file");
                return "";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error extracting text from PDF");
                return "";
            }
        }

        private string? ExtractTextFromWordDocument(Stream wordStream)
        {
            try
            {
                // Reset stream position to beginning
                wordStream.Position = 0;
                
                // Check if stream has valid content
                if (wordStream.Length == 0)
                {
                    _logger.LogWarning("Word document stream is empty");
                    return "";
                }

                using var document = WordprocessingDocument.Open(wordStream, false);
                var body = document.MainDocumentPart?.Document?.Body;
                
                if (body == null)
                {
                    _logger.LogWarning("Word document has no body content");
                    return "";
                }

                var textBuilder = new System.Text.StringBuilder();
                int paragraphCount = 0;
                
                // Extract text from all paragraphs
                foreach (var paragraph in body.Descendants<Paragraph>())
                {
                    try
                    {
                        var paragraphText = paragraph.InnerText;
                        if (!string.IsNullOrWhiteSpace(paragraphText))
                        {
                            textBuilder.AppendLine(paragraphText);
                        }
                        paragraphCount++;
                        
                        // Limit extraction for very large documents
                        if (paragraphCount >= 10000)
                        {
                            _logger.LogInformation($"Word extraction stopped at {paragraphCount} paragraphs to avoid memory issues");
                            break;
                        }
                    }
                    catch (Exception paragraphEx)
                    {
                        _logger.LogWarning(paragraphEx, $"Error extracting text from paragraph {paragraphCount + 1}, skipping");
                        continue;
                    }
                }
                
                var rawText = textBuilder.ToString();
                
                if (string.IsNullOrWhiteSpace(rawText))
                {
                    _logger.LogWarning("Word document extraction completed but no text was found");
                    return ""; // Return empty string to indicate processing was attempted
                }
                
                return CleanupTextFormatting(rawText);
            }
            catch (System.IO.FileFormatException ex)
            {
                _logger.LogWarning(ex, "Word document appears to be corrupted or in unsupported format");
                return "";
            }
            catch (System.IO.IOException ex)
            {
                _logger.LogWarning(ex, "IO error reading Word document");
                return "";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error extracting text from Word document");
                return "";
            }
        }

        private string CleanupTextFormatting(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText))
                return string.Empty;

            // Fix common PDF line-break issues where paragraphs are broken mid-sentence
            // This addresses the issue you mentioned about PDFs having extra line-breaks
            
            var text = rawText;
            
            // Remove excessive whitespace
            text = Regex.Replace(text, @"\r\n|\r|\n", "\n");
            
            // Fix broken paragraphs: merge lines that don't end with sentence-ending punctuation
            // and don't start with paragraph indicators (bullets, numbers, etc.)
            text = Regex.Replace(text, @"(?<![.!?:;\n])\n(?![•\-\*\d\.\s]*[A-Z\n])", " ");
            
            // Clean up multiple spaces
            text = Regex.Replace(text, @"[ \t]+", " ");
            
            // Clean up multiple newlines (preserve paragraph breaks)
            text = Regex.Replace(text, @"\n{3,}", "\n\n");
            
            // Remove leading/trailing whitespace from each line
            var lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                lines[i] = lines[i].Trim();
            }
            
            return string.Join('\n', lines).Trim();
        }

        private async Task<JumpDocument?> ConvertToJumpDocumentAsync(Google.Apis.Drive.v3.Data.File file, string driveId, string driveName)
        {
            try
            {
                // Get folder path
                string folderPath = await GetFolderPathAsync(file.Parents?.FirstOrDefault());

                var document = new JumpDocument
                {
                    GoogleDriveFileId = file.Id,
                    Name = file.Name ?? "Untitled",
                    Description = file.Description ?? string.Empty,
                    MimeType = file.MimeType ?? string.Empty,
                    Size = file.Size ?? 0,
                    CreatedTime = file.CreatedTimeDateTimeOffset?.DateTime ?? DateTime.UtcNow,
                    ModifiedTime = file.ModifiedTimeDateTimeOffset?.DateTime ?? DateTime.UtcNow,
                    LastScanned = DateTime.UtcNow,
                    SourceDrive = driveName,
                    FolderPath = folderPath,
                    WebViewLink = file.WebViewLink ?? string.Empty,
                    DownloadLink = file.ExportLinks?.Values.FirstOrDefault() ?? string.Empty,
                    ThumbnailLink = file.ThumbnailLink ?? string.Empty,
                    HasThumbnail = file.HasThumbnail ?? false
                };

                // Generate tags based on filename, folder, and drive
                document.Tags = GenerateTags(document, folderPath, driveName);

                return document;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error converting file {file.Id} to JumpDocument");
                return null;
            }
        }

        private Task<JumpDocument?> ConvertToJumpDocumentAsync(Google.Apis.Drive.v3.Data.File file, string driveId, string driveName, string folderPath)
        {
            try
            {
                var document = new JumpDocument
                {
                    GoogleDriveFileId = file.Id,
                    Name = file.Name ?? "Untitled",
                    Description = file.Description ?? string.Empty,
                    MimeType = file.MimeType ?? string.Empty,
                    Size = file.Size ?? 0,
                    CreatedTime = file.CreatedTimeDateTimeOffset?.DateTime ?? DateTime.UtcNow,
                    ModifiedTime = file.ModifiedTimeDateTimeOffset?.DateTime ?? DateTime.UtcNow,
                    LastScanned = DateTime.UtcNow,
                    SourceDrive = driveName,
                    FolderPath = folderPath,
                    WebViewLink = file.WebViewLink ?? string.Empty,
                    DownloadLink = file.ExportLinks?.Values.FirstOrDefault() ?? string.Empty,
                    ThumbnailLink = file.ThumbnailLink ?? string.Empty,
                    HasThumbnail = file.HasThumbnail ?? false
                };

                // Generate tags based on filename, folder, and drive
                document.Tags = GenerateTags(document, folderPath, driveName);

                return Task.FromResult<JumpDocument?>(document);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error converting file {file.Id} to JumpDocument");
                return Task.FromResult<JumpDocument?>(null);
            }
        }

        private async Task<string> GetFolderPathAsync(string? parentId)
        {
            if (string.IsNullOrEmpty(parentId))
                return "/";

            try
            {
                var folders = new List<string>();
                string? currentParent = parentId;

                while (!string.IsNullOrEmpty(currentParent))
                {
                    var folder = await _driveService.Files.Get(currentParent).ExecuteAsync();
                    if (folder.Name != null)
                    {
                        folders.Insert(0, folder.Name);
                    }
                    currentParent = folder.Parents?.FirstOrDefault();
                }

                return "/" + string.Join("/", folders);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting folder path for parent {parentId}");
                return "/";
            }
        }

        private List<DocumentTag> GenerateTags(JumpDocument document, string folderPath, string driveName)
        {
            var tags = new List<DocumentTag>();

            // Source Drive tag - Always add this
            tags.Add(new DocumentTag { TagName = driveName, TagCategory = "Drive" });

            // Determine content type based on folder path and filename (using Google Apps Script logic)
            string contentType = DetermineContentType(document.Name, folderPath);
            tags.Add(new DocumentTag { TagName = contentType, TagCategory = "ContentType" });

            // Add folder hierarchy tags
            var folders = folderPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            foreach (var folder in folders.Take(3)) // Limit to first 3 folder levels to avoid clutter
            {
                if (!string.IsNullOrWhiteSpace(folder))
                {
                    tags.Add(new DocumentTag { TagName = folder.Trim(), TagCategory = "Folder" });
                }
            }

            // File format tag based on MIME type and extension
            string fileFormat = DetermineFileFormat(document.MimeType, document.Name);
            if (!string.IsNullOrEmpty(fileFormat))
            {
                tags.Add(new DocumentTag { TagName = fileFormat, TagCategory = "Format" });
            }

            // Size category tag
            string sizeCategory = DetermineSizeCategory(document.Size);
            tags.Add(new DocumentTag { TagName = sizeCategory, TagCategory = "Size" });

            // Quality indicators based on filename patterns
            AddQualityTags(tags, document.Name, folderPath);

            // Series/Franchise detection
            AddSeriesTags(tags, document.Name, folderPath);

            // Text extraction status
            AddTextExtractionTag(tags, document.ExtractedText);

            return tags;
        }

        private string DetermineContentType(string fileName, string folderPath)
        {
            // Combine filename and folder path for analysis (following Google Apps Script logic)
            string combinedPath = $"{folderPath}/{fileName}".ToLowerInvariant();

            // Priority order matters - more specific first
            if (combinedPath.Contains("gauntlet")) return "Gauntlet";
            if (combinedPath.Contains("supplement")) return "Supplement";
            if (combinedPath.Contains("stories") || combinedPath.Contains("story") || combinedPath.Contains("fanfic")) return "Story";
            if (combinedPath.Contains("upload") || combinedPath.Contains("new ")) return "New Upload";
            
            // Default to JumpDoc for standard jump documents
            return "JumpDoc";
        }

        private string DetermineFileFormat(string mimeType, string fileName)
        {
            return mimeType switch
            {
                "application/pdf" => "PDF",
                "application/vnd.google-apps.document" => "Google Doc",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => "Word Doc",
                "text/plain" => "Text",
                _ when fileName.ToLowerInvariant().EndsWith(".rtf") => "RTF",
                _ when fileName.ToLowerInvariant().EndsWith(".odt") => "OpenDocument",
                _ => "Document"
            };
        }

        private string DetermineSizeCategory(long sizeBytes)
        {
            return sizeBytes switch
            {
                < 1024 * 1024 => "Small", // < 1MB
                < 10 * 1024 * 1024 => "Medium", // < 10MB
                < 50 * 1024 * 1024 => "Large", // < 50MB
                _ => "Very Large" // >= 50MB
            };
        }

        private void AddQualityTags(List<DocumentTag> tags, string fileName, string folderPath)
        {
            string fullPath = $"{folderPath}/{fileName}".ToLowerInvariant();

            // Quality indicators
            if (fullPath.Contains("wip") || fullPath.Contains("work in progress") || fullPath.Contains("incomplete"))
                tags.Add(new DocumentTag { TagName = "Work in Progress", TagCategory = "Status" });
            
            if (fullPath.Contains("complete") || fullPath.Contains("finished"))
                tags.Add(new DocumentTag { TagName = "Complete", TagCategory = "Status" });
                
            if (fullPath.Contains("draft") || fullPath.Contains("rough"))
                tags.Add(new DocumentTag { TagName = "Draft", TagCategory = "Status" });

            if (fullPath.Contains("nsfw") || fullPath.Contains("adult"))
                tags.Add(new DocumentTag { TagName = "NSFW", TagCategory = "Content" });

            if (fullPath.Contains("sfw") || fullPath.Contains("safe"))
                tags.Add(new DocumentTag { TagName = "SFW", TagCategory = "Content" });

            // Version indicators
            if (fullPath.Contains("v1.") || fullPath.Contains("version 1"))
                tags.Add(new DocumentTag { TagName = "v1.x", TagCategory = "Version" });
            if (fullPath.Contains("v2.") || fullPath.Contains("version 2"))
                tags.Add(new DocumentTag { TagName = "v2.x", TagCategory = "Version" });
        }

        private void AddSeriesTags(List<DocumentTag> tags, string fileName, string folderPath)
        {
            string fullPath = $"{folderPath}/{fileName}".ToLowerInvariant();

            // Popular franchises/series detection
            var franchises = new Dictionary<string, string[]>
            {
                ["Marvel"] = new[] { "marvel", "x-men", "avengers", "spider-man", "iron man", "captain america" },
                ["DC Comics"] = new[] { "batman", "superman", "wonder woman", "justice league", "flash", "green lantern" },
                ["Star Wars"] = new[] { "star wars", "jedi", "sith", "clone wars" },
                ["Pokemon"] = new[] { "pokemon", "pokémon" },
                ["Naruto"] = new[] { "naruto", "konoha", "ninja" },
                ["Harry Potter"] = new[] { "harry potter", "hogwarts", "wizarding world" },
                ["Warhammer"] = new[] { "warhammer", "40k", "space marine" },
                ["Generic"] = new[] { "generic" }
            };

            foreach (var franchise in franchises)
            {
                if (franchise.Value.Any(keyword => fullPath.Contains(keyword)))
                {
                    tags.Add(new DocumentTag { TagName = franchise.Key, TagCategory = "Franchise" });
                    break; // Only add the first match to avoid duplicates
                }
            }
        }

        private void AddTextExtractionTag(List<DocumentTag> tags, string? extractedText)
        {
            // Only add HasText tag if there is actually extracted text
            if (!string.IsNullOrEmpty(extractedText))
            {
                tags.Add(new DocumentTag { TagName = "HasText", TagCategory = "Extraction" });
            }
        }

        public async Task<object> DebugFilePropertiesAsync(string fileId)
        {
            var services = new[] { _publicDriveService, _driveService };
            var serviceNames = new[] { "PublicAPI", "ServiceAccount" };
            
            for (int i = 0; i < services.Length; i++)
            {
                var service = services[i];
                var serviceName = serviceNames[i];
                
                try
                {
                    var request = service.Files.Get(fileId);
                    request.Fields = "*"; // Get all fields
                    var file = await request.ExecuteAsync();
                    
                    // Try to get export links for Google Docs format
                    var exportLinks = new Dictionary<string, string>();
                    
                    // Check if this can be exported as different formats
                    try
                    {
                        if (file.MimeType?.Contains("google-apps") == true)
                        {
                            // It's a Google Doc, get export links
                            exportLinks = file.ExportLinks?.ToDictionary(kv => kv.Key, kv => kv.Value) ?? new Dictionary<string, string>();
                        }
                        else
                        {
                            // Try to see if we can export it anyway (some Word docs converted to Google Docs keep .docx extension)
                            try 
                            {
                                var testExport = service.Files.Export(fileId, "text/plain");
                                exportLinks["text/plain"] = $"Available via Export API";
                            }
                            catch { /* Export not available */ }
                        }
                    }
                    catch { /* Export check failed */ }
                    
                    return new
                    {
                        success = true,
                        serviceName,
                        fileId,
                        name = file.Name,
                        mimeType = file.MimeType,
                        size = file.Size,
                        exportLinks = exportLinks,
                        hasExportLinks = exportLinks.Count > 0,
                        downloadUrl = file.WebContentLink,
                        canDownload = !string.IsNullOrEmpty(file.WebContentLink),
                        parents = file.Parents,
                        originalFilename = file.OriginalFilename,
                        fileExtension = file.FileExtension,
                        isGoogleDoc = file.MimeType?.Contains("google-apps") == true,
                        viewLink = file.WebViewLink
                    };
                }
                catch (Exception ex)
                {
                    if (i == services.Length - 1)
                    {
                        return new { success = false, error = ex.Message, serviceName };
                    }
                }
            }
            
            return new { success = false, error = "All authentication methods failed" };
        }
    }
}
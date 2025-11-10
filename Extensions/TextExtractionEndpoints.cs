using Microsoft.EntityFrameworkCore;
using JumpChainSearch.Data;
using JumpChainSearch.Services;
using System.Text;

namespace JumpChainSearch.Extensions;

/// <summary>
/// Endpoints for text extraction, debugging, and testing
/// </summary>
public static class TextExtractionEndpoints
{
    public static RouteGroupBuilder MapTextExtractionEndpoints(this RouteGroupBuilder group)
    {
        // Main extraction endpoints
        group.MapGet("/{documentId:int}", GetExtractedText);
        group.MapGet("/debug/{fileId}", DebugExtract);
        group.MapPost("/test-save/{documentId:int}", TestSaveText);
        group.MapGet("/test/{documentId:int}", TestTextExtraction);
        group.MapPost("/bulk", BulkExtractText);
        group.MapGet("/status", DebugExtractionStatus);
        group.MapPost("/test-few/{limit:int?}", TestExtractFew);
        group.MapGet("/diagnose/{documentId:int}", DiagnoseDocument);
        group.MapPost("/extract-save/{documentId:int}", ExtractAndSave);
        group.MapGet("/direct-export/{fileId}", DirectDriveExport);
        group.MapPost("/re-extract-all", ReExtractWithImprovedMethods);
        group.MapPost("/re-extract/{documentId:int}", ReExtractDocument);
        group.MapGet("/check-status/{limit:int?}", CheckExtractionStatus);
        group.MapGet("/batch-tracking", BatchExtractWithTracking);
        group.MapGet("/ocr-candidates", GetOcrCandidates);
        group.MapGet("/analyze-zero-filesize", AnalyzeZeroFilesize);
        
        return group;
    }

    /// <summary>
    /// Get extracted text for a specific document
    /// </summary>
    private static async Task<IResult> GetExtractedText(JumpChainDbContext context, int documentId)
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
                    HasExtractedText = !string.IsNullOrEmpty(d.ExtractedText),
                    ExtractedTextLength = d.ExtractedText != null ? d.ExtractedText.Length : 0,
                    ExtractedTextPreview = d.ExtractedText != null && d.ExtractedText.Length > 0 
                        ? d.ExtractedText.Substring(0, Math.Min(500, d.ExtractedText.Length)) + (d.ExtractedText.Length > 500 ? "..." : "")
                        : "No extracted text available",
                    FullExtractedText = d.ExtractedText
                })
                .FirstOrDefaultAsync();

            if (document == null)
            {
                return Results.NotFound(new { success = false, error = "Document not found" });
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

    /// <summary>
    /// Direct text extraction test for a specific Google Drive file ID
    /// </summary>
    private static async Task<IResult> DebugExtract(IGoogleDriveService driveService, string fileId)
    {
        try
        {
            var extractedText = await driveService.ExtractTextFromDocumentAsync(fileId);
            
            var hasText = !string.IsNullOrEmpty(extractedText);
            var textLength = extractedText?.Length ?? 0;
            var textPreview = hasText && textLength > 0 
                ? extractedText!.Substring(0, Math.Min(500, textLength)) + (textLength > 500 ? "..." : "")
                : "No text extracted";
            
            return Results.Ok(new {
                success = true,
                fileId,
                hasText,
                textLength,
                textPreview,
                fullText = extractedText ?? ""
            });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { 
                success = false, 
                error = ex.Message,
                details = ex.InnerException?.Message ?? ""
            });
        }
    }

    /// <summary>
    /// Test database persistence by manually setting extracted text
    /// </summary>
    private static async Task<IResult> TestSaveText(
        JumpChainDbContext context, 
        int documentId, 
        string testText = "This is a test of text extraction persistence.")
    {
        try
        {
            var document = await context.JumpDocuments.FindAsync(documentId);
            if (document == null)
            {
                return Results.NotFound(new { success = false, error = "Document not found" });
            }

            var originalText = document.ExtractedText;
            document.ExtractedText = testText;
            
            var changes = await context.SaveChangesAsync();
            
            // Verify the change was saved
            var verificationDoc = await context.JumpDocuments.FindAsync(documentId);
            
            return Results.Ok(new {
                success = true,
                documentId,
                originalTextLength = originalText?.Length ?? 0,
                newTextLength = testText.Length,
                changesSaved = changes,
                verifiedText = verificationDoc?.ExtractedText ?? "",
                verificationMatches = verificationDoc?.ExtractedText == testText
            });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { 
                success = false, 
                error = ex.Message,
                details = ex.InnerException?.Message ?? ""
            });
        }
    }

    /// <summary>
    /// Test text extraction for a specific document with detailed debugging
    /// </summary>
    private static async Task<IResult> TestTextExtraction(
        JumpChainDbContext context, 
        IGoogleDriveService driveService, 
        int documentId)
    {
        try
        {
            var document = await context.JumpDocuments.FindAsync(documentId);
            if (document == null)
            {
                return Results.NotFound(new { success = false, message = $"Document {documentId} not found" });
            }

            var fileId = document.GoogleDriveFileId ?? "";
            if (string.IsNullOrEmpty(fileId))
            {
                return Results.BadRequest(new { success = false, message = "Document has no Google Drive file ID" });
            }

            var extractedText = await driveService.ExtractTextFromDocumentAsync(fileId);
            
            var textLength = extractedText?.Length ?? 0;
            var preview = textLength > 0 
                ? extractedText!.Substring(0, Math.Min(textLength, 500)) + (textLength > 500 ? "..." : "")
                : "No text extracted";
            
            return Results.Ok(new {
                success = true,
                document = new { 
                    document.Id, 
                    document.Name, 
                    document.MimeType, 
                    document.Size,
                    document.GoogleDriveFileId,
                    hasExistingText = !string.IsNullOrEmpty(document.ExtractedText)
                },
                extractedText = preview,
                fullTextLength = textLength,
                extractionSuccessful = textLength > 0
            });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { 
                success = false, 
                error = ex.Message,  
                details = ex.InnerException?.Message ?? ""
            });
        }
    }

    /// <summary>
    /// Bulk extract text from all documents without extracted text
    /// </summary>
    private static async Task<IResult> BulkExtractText(
        JumpChainDbContext context, 
        IGoogleDriveService driveService, 
        int? batchSize = 50)
    {
        try
        {
            // Set batch size with default and max limits
            var actualBatchSize = Math.Min(batchSize ?? 50, 100); // Cap at 100

            // Get documents that don't have extracted text yet (null = unprocessed)
            var documentsToProcess = await context.JumpDocuments
                .Where(d => d.ExtractedText == null)
                .Take(actualBatchSize)
                .ToListAsync();

            if (!documentsToProcess.Any())
            {
                return Results.Ok(new {
                    success = true,
                    message = "All documents already have extracted text",
                    processedCount = 0,
                    totalDocuments = await context.JumpDocuments.CountAsync()
                });
            }

            int successCount = 0;
            int errorCount = 0;
            int timeoutCount = 0;
            var results = new List<object>();

            foreach (var document in documentsToProcess)
            {
                CancellationTokenSource? cts = null;
                try
                {
                    cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                    
                    var fileId = document.GoogleDriveFileId ?? "";
                    if (string.IsNullOrEmpty(fileId))
                    {
                        document.ExtractedText = "";
                        errorCount++;
                        continue;
                    }

                    var extractedText = await driveService.ExtractTextFromDocumentAsync(fileId)
                        .WaitAsync(cts.Token);
                    
                    if (extractedText != null)
                    {
                        document.ExtractedText = extractedText;
                        successCount++;
                        
                        results.Add(new {
                            documentId = document.Id,
                            name = document.Name ?? "Unknown",
                            mimeType = document.MimeType ?? "Unknown",
                            textLength = extractedText.Length,
                            status = extractedText.Length > 0 ? "success" : "processed_no_text"
                        });
                    }
                    else
                    {
                        document.ExtractedText = "";
                        errorCount++;
                        results.Add(new {
                            documentId = document.Id,
                            name = document.Name ?? "Unknown",
                            mimeType = document.MimeType ?? "Unknown",
                            status = "extraction_failed"
                        });
                    }
                    
                    await context.SaveChangesAsync();
                    await Task.Delay(350);
                }
                catch (OperationCanceledException) when (cts?.IsCancellationRequested == true)
                {
                    timeoutCount++;
                    document.ExtractedText = "";
                    
                    try 
                    {
                        await context.SaveChangesAsync();
                        results.Add(new {
                            documentId = document.Id,
                            name = document.Name ?? "Unknown",
                            mimeType = document.MimeType ?? "Unknown",
                            status = "timeout",
                            error = "Document processing timed out after 90 seconds"
                        });
                    }
                    catch
                    {
                        document.ExtractedText = null;
                    }
                }
                catch (Exception)
                {
                    errorCount++;
                    document.ExtractedText = "";
                    
                    try 
                    {
                        await context.SaveChangesAsync();
                    }
                    catch
                    {
                        document.ExtractedText = null;
                    }
                }
                finally
                {
                    cts?.Dispose();
                }
            }

            await context.SaveChangesAsync();

            var remainingDocuments = await context.JumpDocuments
                .CountAsync(d => string.IsNullOrEmpty(d.ExtractedText));

            return Results.Ok(new {
                success = true,
                message = $"Processed {documentsToProcess.Count} documents",
                successCount,
                errorCount,
                timeoutCount,
                remainingDocuments,
                hasMoreToProcess = remainingDocuments > 0,
                results = results.Take(10)
            });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { 
                success = false, 
                error = ex.Message,
                details = ex.InnerException?.Message ?? ""
            });
        }
    }

    /// <summary>
    /// Debug endpoint to check extraction status
    /// </summary>
    private static async Task<IResult> DebugExtractionStatus(JumpChainDbContext context)
    {
        var total = await context.JumpDocuments.CountAsync();
        var withText = await context.JumpDocuments.CountAsync(d => !string.IsNullOrEmpty(d.ExtractedText));
        var nullText = await context.JumpDocuments.CountAsync(d => d.ExtractedText == null);
        var emptyText = await context.JumpDocuments.CountAsync(d => d.ExtractedText == "");
        var actuallyExtracted = await context.JumpDocuments.CountAsync(d => !string.IsNullOrEmpty(d.ExtractedText) && d.ExtractedText.Length > 10);
        
        var sampleUnprocessed = await context.JumpDocuments
            .Where(d => d.ExtractedText == null)
            .Take(3)
            .Select(d => new { 
                d.Id, 
                d.Name, 
                d.MimeType, 
                ExtractedTextStatus = d.ExtractedText == null ? "null" : d.ExtractedText == "" ? "empty" : "has_text" 
            })
            .ToListAsync();

        return Results.Ok(new {
            total,
            withText,
            nullText,
            emptyText,
            actuallyExtracted,
            unprocessed = nullText,
            sampleUnprocessed
        });
    }

    /// <summary>
    /// Test endpoint to extract text from a few specific documents
    /// </summary>
    private static async Task<IResult> TestExtractFew(
        JumpChainDbContext context, 
        IGoogleDriveService driveService, 
        int? limit = 3)
    {
        try
        {
            var documentsToTest = await context.JumpDocuments
                .Where(d => d.Id == 1 || (d.ExtractedText == null && d.Id != 1))
                .Take(limit ?? 3)
                .ToListAsync();

            if (!documentsToTest.Any())
            {
                return Results.Ok(new { 
                    success = false, 
                    message = "No documents found without extracted text" 
                });
            }

            var results = new List<object>();

            foreach (var document in documentsToTest)
            {
                try
                {
                    var fileId = document.GoogleDriveFileId ?? "";
                    if (string.IsNullOrEmpty(fileId))
                    {
                        results.Add(new {
                            documentId = document.Id,
                            name = document.Name ?? "Unknown",
                            error = "No Google Drive file ID"
                        });
                        continue;
                    }

                    var extractedText = await driveService.ExtractTextFromDocumentAsync(fileId);
                    
                    var result = new
                    {
                        documentId = document.Id,
                        name = document.Name ?? "Unknown",
                        mimeType = document.MimeType ?? "Unknown",
                        fileId = document.GoogleDriveFileId ?? "",
                        extractionAttempted = true,
                        extractedTextLength = extractedText?.Length ?? 0,
                        extractionSuccessful = !string.IsNullOrEmpty(extractedText),
                        preview = extractedText != null && extractedText.Length > 100 
                            ? extractedText.Substring(0, 100) + "..." 
                            : extractedText ?? "No text extracted",
                        errorMessage = (string?)null
                    };

                    if (!string.IsNullOrEmpty(extractedText))
                    {
                        document.ExtractedText = extractedText;
                        await context.SaveChangesAsync();
                    }
                    else
                    {
                        document.ExtractedText = "";
                        await context.SaveChangesAsync();
                    }

                    results.Add(result);
                }
                catch (Exception ex)
                {
                    results.Add(new
                    {
                        documentId = document.Id,
                        name = document.Name ?? "Unknown",
                        mimeType = document.MimeType ?? "Unknown",
                        fileId = document.GoogleDriveFileId ?? "",
                        extractionAttempted = true,
                        extractedTextLength = 0,
                        extractionSuccessful = false,
                        preview = "Error during extraction",
                        errorMessage = ex.Message
                    });
                    
                    document.ExtractedText = "";
                    await context.SaveChangesAsync();
                }
            }

            return Results.Ok(new {
                success = true,
                message = $"Tested text extraction on {results.Count} documents",
                results = results
            });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { 
                success = false, 
                error = ex.Message,
                details = ex.InnerException?.Message ?? ""
            });
        }
    }

    /// <summary>
    /// Diagnose a problematic document without extracting text
    /// </summary>
    private static async Task<IResult> DiagnoseDocument(JumpChainDbContext context, int documentId)
    {
        try
        {
            var document = await context.JumpDocuments.FindAsync(documentId);
            if (document == null)
            {
                return Results.NotFound(new { success = false, error = $"Document {documentId} not found" });
            }

            return Results.Ok(new {
                documentId = documentId,
                fileName = document.Name ?? "Unknown",
                mimeType = document.MimeType ?? "Unknown",
                googleDriveFileId = document.GoogleDriveFileId ?? "",
                size = document.Size,
                hasExtractedText = !string.IsNullOrEmpty(document.ExtractedText),
                extractedTextLength = document.ExtractedText?.Length ?? 0,
                extractionMethod = document.ExtractionMethod ?? "Unknown"
            });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { 
                success = false, 
                error = ex.Message,
                details = ex.InnerException?.Message ?? ""
            });
        }
    }

    /// <summary>
    /// Extract text and save to database for a specific document
    /// </summary>
    private static async Task<IResult> ExtractAndSave(JumpChainDbContext context, int documentId)
    {
        try
        {
            var document = await context.JumpDocuments.FindAsync(documentId);
            if (document == null)
            {
                return Results.NotFound(new { success = false, error = $"Document {documentId} not found" });
            }

            return Results.Ok(new {
                success = false,
                message = "This endpoint requires Google Drive service integration",
                documentId = documentId,
                fileName = document.Name ?? "Unknown"
            });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { 
                success = false, 
                error = ex.Message,
                details = ex.InnerException?.Message ?? ""
            });
        }
    }

    /// <summary>
    /// Direct Google Drive API test endpoint
    /// </summary>
    private static Task<IResult> DirectDriveExport(string fileId)
    {
        return Task.FromResult(Results.Ok(new {
            success = false,
            message = "This endpoint requires direct Google Drive API integration",
            fileId = fileId
        }));
    }

    /// <summary>
    /// Batch re-extract text using improved methods
    /// </summary>
    private static Task<IResult> ReExtractWithImprovedMethods(
        JumpChainDbContext context, 
        IGoogleDriveService driveService)
    {
        return Task.FromResult(Results.Ok(new {
            success = false,
            message = "Batch re-extraction feature pending implementation"
        }));
    }

    /// <summary>
    /// Re-extract text for a specific document using improved methods
    /// </summary>
    private static Task<IResult> ReExtractDocument(
        JumpChainDbContext context, 
        IGoogleDriveService driveService, 
        int documentId)
    {
        return Task.FromResult(Results.Ok(new {
            success = false,
            message = "Re-extraction feature pending implementation",
            documentId = documentId
        }));
    }

    /// <summary>
    /// Check extraction status for a few documents
    /// </summary>
    private static async Task<IResult> CheckExtractionStatus(JumpChainDbContext context, int? limit = 10)
    {
        try
        {
            var documents = await context.JumpDocuments
                .Where(d => d.ExtractedText != null && d.ExtractedText != "")
                .Select(d => new {
                    d.Id,
                    d.Name,
                    d.MimeType,
                    TextLength = d.ExtractedText!.Length,
                    d.ExtractionMethod,
                    HasSpaces = d.ExtractedText!.Contains(" "),
                    Preview = d.ExtractedText!.Substring(0, Math.Min(100, d.ExtractedText.Length))
                })
                .Take(limit ?? 10)
                .ToListAsync();

            return Results.Ok(new {
                success = true,
                documentsChecked = documents.Count,
                documents = documents
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
    /// Batch extract text with OCR candidate tracking
    /// </summary>
    private static async Task<IResult> BatchExtractWithTracking(
        JumpChainDbContext context, 
        int startId = 1, 
        int count = 50)
    {
        try
        {
            var documentsToProcess = await context.JumpDocuments
                .Where(d => d.Id >= startId && d.ExtractedText == null)
                .Take(count)
                .ToListAsync();

            return Results.Ok(new {
                message = $"Found {documentsToProcess.Count} documents to process",
                startId = startId,
                count = documentsToProcess.Count,
                documentIds = documentsToProcess.Select(d => d.Id).ToList()
            });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { 
                error = ex.Message,
                details = ex.InnerException?.Message ?? ""
            });
        }
    }

    /// <summary>
    /// Get OCR candidates and low-extraction documents
    /// </summary>
    private static async Task<IResult> GetOcrCandidates(JumpChainDbContext context)
    {
        try
        {
            var ocrCandidates = await context.JumpDocuments
                .Where(d => !string.IsNullOrEmpty(d.ExtractedText) && 
                           d.ExtractedText.Length < 1000 && 
                           d.Size > 100000)
                .OrderBy(d => d.ExtractedText!.Length)
                .Select(d => new {
                    d.Id,
                    d.Name,
                    d.Size,
                    TextLength = d.ExtractedText!.Length,
                    d.ExtractionMethod,
                    d.MimeType,
                    d.FolderPath
                })
                .Take(20)
                .ToListAsync();

            var lowExtractionDocs = await context.JumpDocuments
                .Where(d => !string.IsNullOrEmpty(d.ExtractedText) && 
                           d.ExtractedText.Length > 0 && 
                           d.ExtractedText.Length < 2000 &&
                           d.Size > 10000)
                .OrderBy(d => d.ExtractedText!.Length)
                .Select(d => new {
                    d.Id,
                    d.Name,
                    d.Size,
                    TextLength = d.ExtractedText!.Length,
                    d.ExtractionMethod,
                    d.MimeType,
                    d.FolderPath
                })
                .Take(20)
                .ToListAsync();

            return Results.Ok(new {
                ocrCandidateCount = ocrCandidates.Count,
                lowExtractionCount = lowExtractionDocs.Count,
                ocrCandidates = ocrCandidates,
                lowExtractionDocuments = lowExtractionDocs
            });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { 
                error = ex.Message
            });
        }
    }

    /// <summary>
    /// Analyze zero-filesize documents and their patterns
    /// </summary>
    private static async Task<IResult> AnalyzeZeroFilesize(JumpChainDbContext context)
    {
        try
        {
            var zeroSizeDocs = await context.JumpDocuments
                .Where(d => d.Size == 0)
                .OrderBy(d => d.Id)
                .Select(d => new {
                    d.Id,
                    d.Name,
                    d.SourceDrive,
                    d.FolderPath,
                    d.MimeType,
                    d.GoogleDriveFileId,
                    d.Size
                })
                .ToListAsync();

            var driveAnalysis = zeroSizeDocs
                .GroupBy(d => d.SourceDrive ?? "Unknown")
                .Select(g => new {
                    Drive = g.Key,
                    Count = g.Count(),
                    DocumentIds = g.Select(d => d.Id).OrderBy(id => id).Take(10).ToList()
                })
                .OrderByDescending(g => g.Count)
                .ToList();

            return Results.Ok(new {
                totalZeroSize = zeroSizeDocs.Count,
                driveAnalysis = driveAnalysis,
                sampleDocuments = zeroSizeDocs.Take(10)
            });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { 
                error = ex.Message
            });
        }
    }
}

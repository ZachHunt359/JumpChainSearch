using JumpChainSearch.Data;
using JumpChainSearch.Models;
using JumpChainSearch.Services;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace JumpChainSearch.Extensions;

public static class BatchProcessingEndpoints
{
    private static string? _currentSessionId = null;
    private static bool _isProcessing = false;
    private static readonly object _lockObject = new();

    public static RouteGroupBuilder MapBatchProcessingEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/status", GetExtractionStatus);
        group.MapPost("/start", StartBatchExtraction);
        group.MapPost("/stop", StopBatchExtraction);
        group.MapGet("/session/{sessionId}", GetSessionInfo);
        
        return group;
    }

    private static async Task<IResult> GetExtractionStatus(JumpChainDbContext context)
    {
        try
        {
            var stats = await context.JumpDocuments
                .GroupBy(d => d.MimeType)
                .Select(g => new
                {
                    MimeType = g.Key,
                    Total = g.Count(),
                    Extracted = g.Count(d => d.ExtractedText != null && d.ExtractedText != string.Empty),
                    NotAttempted = g.Count(d => d.ExtractionMethod == null),
                    Failed = g.Count(d => d.ExtractionMethod != null && (d.ExtractedText == null || d.ExtractedText == string.Empty))
                })
                .ToListAsync();

            var totalDocs = stats.Sum(s => s.Total);
            var totalExtracted = stats.Sum(s => s.Extracted);
            var totalNotAttempted = stats.Sum(s => s.NotAttempted);
            var totalFailed = stats.Sum(s => s.Failed);

            lock (_lockObject)
            {
                return Results.Ok(new
                {
                    success = true,
                    isProcessing = _isProcessing,
                    currentSessionId = _currentSessionId,
                    overall = new
                    {
                        total = totalDocs,
                        extracted = totalExtracted,
                        notAttempted = totalNotAttempted,
                        failed = totalFailed,
                        percentage = totalDocs > 0 ? Math.Round(100.0 * totalExtracted / totalDocs, 2) : 0
                    },
                    byType = stats
                });
            }
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { success = false, error = ex.Message });
        }
    }

    private static Task<IResult> StartBatchExtraction(
        JumpChainDbContext context,
        IGoogleDriveService driveService,
        int batchSize = 10,  // Reduced from 50 to 10 to conserve memory during OCR
        string? mimeTypeFilter = null)
    {
        lock (_lockObject)
        {
            if (_isProcessing)
            {
                return Task.FromResult<IResult>(Results.BadRequest(new
                {
                    success = false,
                    error = "Batch processing is already running",
                    currentSessionId = _currentSessionId
                }));
            }

            _isProcessing = true;
            _currentSessionId = Guid.NewGuid().ToString();
        }

        var sessionId = _currentSessionId!;
        var connectionString = context.Database.GetConnectionString();
        var logDir = Path.Combine(Directory.GetCurrentDirectory(), "logs");
        Directory.CreateDirectory(logDir);
        var logFile = Path.Combine(logDir, $"batch-extraction-{sessionId}.log");

        // Start background processing
        _ = Task.Run(async () =>
        {
            try
            {
                await File.AppendAllTextAsync(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Starting batch extraction session {sessionId}\n");
                await File.AppendAllTextAsync(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Batch size: {batchSize}, Filter: {mimeTypeFilter ?? "All types"}\n");

                var totalProcessed = 0;
                var totalSuccess = 0;
                var totalFailed = 0;

                while (true)
                {
                    using var batchContext = new JumpChainDbContext(new DbContextOptionsBuilder<JumpChainDbContext>()
                        .UseSqlite(connectionString)
                        .Options);

                    // Get next batch of documents that need processing
                    // ExtractionMethod IS NULL means we haven't tried extracting yet
                    // ExtractionMethod starts with "REPROCESS" means smart reprocessing needed
                    var query = batchContext.JumpDocuments
                        .Where(d => d.ExtractionMethod == null || d.ExtractionMethod.StartsWith("REPROCESS|"));

                    if (!string.IsNullOrEmpty(mimeTypeFilter))
                    {
                        query = query.Where(d => d.MimeType == mimeTypeFilter);
                    }

                    var batch = await query.Take(batchSize).ToListAsync();

                    if (batch.Count == 0)
                    {
                        await File.AppendAllTextAsync(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] No more documents to process\n");
                        break;
                    }

                    await File.AppendAllTextAsync(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Processing batch of {batch.Count} documents\n");

                    var docCount = 0;
                    foreach (var doc in batch)
                    {
                        try
                        {
                            // Check if this is a smart reprocess
                            bool isReprocess = doc.ExtractionMethod?.StartsWith("REPROCESS|") == true;
                            string? originalText = null;
                            int originalLength = 0;
                            string? originalMethod = null;
                            
                            if (isReprocess && doc.ExtractionMethod != null)
                            {
                                var parts = doc.ExtractionMethod.Split('|');
                                if (parts.Length >= 3)
                                {
                                    int.TryParse(parts[1], out originalLength);
                                    originalMethod = parts[2];
                                    originalText = doc.ExtractedText; // Original text still intact
                                }
                            }
                            
                            var (text, method) = await driveService.ExtractTextWithMethodAsync(doc.GoogleDriveFileId);

                            if (!string.IsNullOrEmpty(text))
                            {
                                // Smart comparison logic for reprocess mode
                                if (isReprocess && originalText != null)
                                {
                                    int newLength = text.Length;
                                    bool useNewExtraction = false;
                                    string comparisonReason = "";
                                    
                                    // Decision logic: prefer longer text, then better method
                                    if (newLength > originalLength * 1.2) // New text is 20% longer
                                    {
                                        useNewExtraction = true;
                                        comparisonReason = $"new text longer ({newLength} vs {originalLength})";
                                    }
                                    else if (newLength < originalLength * 0.5) // New text is half as short
                                    {
                                        useNewExtraction = false;
                                        comparisonReason = $"keeping original (new too short: {newLength} vs {originalLength})";
                                    }
                                    else if (!string.IsNullOrEmpty(method) && method.Contains("improved_pdfpig") && originalMethod?.Contains("basic") == true)
                                    {
                                        useNewExtraction = true;
                                        comparisonReason = "better extraction method";
                                    }
                                    else if (newLength >= originalLength)
                                    {
                                        useNewExtraction = true;
                                        comparisonReason = $"new text equal/longer ({newLength} vs {originalLength})";
                                    }
                                    else
                                    {
                                        useNewExtraction = false;
                                        comparisonReason = $"keeping original ({originalLength} vs {newLength})";
                                    }
                                    
                                    if (useNewExtraction)
                                    {
                                        doc.ExtractedText = text;
                                        doc.ExtractionMethod = method;
                                        await File.AppendAllTextAsync(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] REPROCESS UPGRADE: Doc {doc.Id} ({doc.Name}) - {comparisonReason}\n");
                                    }
                                    else
                                    {
                                        // Keep original text, just update method to show it was checked
                                        doc.ExtractionMethod = $"{originalMethod}_rechecked";
                                        await File.AppendAllTextAsync(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] REPROCESS KEPT ORIGINAL: Doc {doc.Id} ({doc.Name}) - {comparisonReason}\n");
                                    }
                                }
                                else
                                {
                                    // Normal first-time extraction
                                    doc.ExtractedText = text;
                                    doc.ExtractionMethod = method;
                                }
                                
                                // Add "Has Text" tag
                                var existingTag = await batchContext.DocumentTags
                                    .FirstOrDefaultAsync(t => t.JumpDocumentId == doc.Id && t.TagName == "Has Text");
                                if (existingTag == null)
                                {
                                    batchContext.DocumentTags.Add(new DocumentTag
                                    {
                                        TagName = "Has Text",
                                        TagCategory = "Extraction",
                                        JumpDocumentId = doc.Id
                                    });
                                }
                                
                                totalSuccess++;
                                await File.AppendAllTextAsync(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] SUCCESS: Doc {doc.Id} ({doc.Name}) - {text.Length} chars via {method}\n");
                            }
                            else
                            {
                                // Mark as attempted but failed (so we don't retry unnecessarily)
                                doc.ExtractionMethod = "extraction_failed";
                                totalFailed++;
                                await File.AppendAllTextAsync(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] FAILED: Doc {doc.Id} ({doc.Name}) - No text extracted\n");
                            }

                            totalProcessed++;
                            docCount++;
                            
                            // Force garbage collection after each document to reduce memory usage
                            GC.Collect();
                            GC.WaitForPendingFinalizers();
                            
                            // Save every 10 documents to prevent data loss
                            if (docCount % 10 == 0)
                            {
                                await batchContext.SaveChangesAsync();
                                await File.AppendAllTextAsync(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Progress saved ({docCount}/{batch.Count}). Total: {totalProcessed}, Success: {totalSuccess}, Failed: {totalFailed}\n");
                            }
                        }
                        catch (Exception ex)
                        {
                            // Mark as attempted with error (so we don't retry unnecessarily)
                            doc.ExtractionMethod = $"error: {ex.Message.Substring(0, Math.Min(100, ex.Message.Length))}";
                            totalFailed++;
                            totalProcessed++;
                            await File.AppendAllTextAsync(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ERROR: Doc {doc.Id} ({doc.Name}) - {ex.Message}\n");
                        }
                    }

                    // Final save for remaining documents in batch
                    await batchContext.SaveChangesAsync();
                    await File.AppendAllTextAsync(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Batch completed and saved. Total: {totalProcessed}, Success: {totalSuccess}, Failed: {totalFailed}\n");
                }

                await File.AppendAllTextAsync(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Extraction session completed\n");
                await File.AppendAllTextAsync(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Final stats - Processed: {totalProcessed}, Success: {totalSuccess}, Failed: {totalFailed}\n");
            }
            catch (Exception ex)
            {
                await File.AppendAllTextAsync(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] FATAL ERROR: {ex.Message}\n{ex.StackTrace}\n");
            }
            finally
            {
                lock (_lockObject)
                {
                    _isProcessing = false;
                    _currentSessionId = null;
                }
            }
        });

        return Task.FromResult<IResult>(Results.Ok(new
        {
            success = true,
            message = "Batch extraction started",
            sessionId,
            logFile
        }));
    }

    private static Task<IResult> StopBatchExtraction()
    {
        lock (_lockObject)
        {
            if (!_isProcessing)
            {
                return Task.FromResult<IResult>(Results.BadRequest(new
                {
                    success = false,
                    error = "No batch processing is currently running"
                }));
            }

            _isProcessing = false;
            var stoppedSessionId = _currentSessionId;
            _currentSessionId = null;

            return Task.FromResult<IResult>(Results.Ok(new
            {
                success = true,
                message = "Batch extraction stop requested",
                stoppedSessionId
            }));
        }
    }

    private static async Task<IResult> GetSessionInfo(string sessionId)
    {
        try
        {
            var logDir = Path.Combine(Directory.GetCurrentDirectory(), "logs");
            var logFile = Path.Combine(logDir, $"batch-extraction-{sessionId}.log");

            if (!File.Exists(logFile))
            {
                return Results.NotFound(new { success = false, error = "Session log not found" });
            }

            var logContents = await File.ReadAllTextAsync(logFile);
            var lines = logContents.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            return Results.Ok(new
            {
                success = true,
                sessionId,
                logFile,
                lineCount = lines.Length,
                lastLines = lines.TakeLast(20).Where(l => !string.IsNullOrWhiteSpace(l))
            });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { success = false, error = ex.Message });
        }
    }
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using JumpChainSearch.Data;
using JumpChainSearch.Services;

namespace JumpChainSearch.Extensions;

/// <summary>
/// Legacy batch processing endpoints with session tracking and file-based checkpointing.
/// These endpoints manage batch text extraction sessions with persistent state.
/// </summary>
public static class SessionManagementEndpoints
{
    public static RouteGroupBuilder MapSessionManagementEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/start", StartBatchProcessing);
        group.MapPost("/process/{sessionId}/{batchSize:int?}", ProcessBatch);
        group.MapGet("/status/{sessionId}", GetBatchStatus);
        group.MapGet("/list", ListSessions);
        group.MapPost("/resume/{sessionId}", ResumeSession);
        return group;
    }

    private static async Task<IResult> StartBatchProcessing(IServiceProvider serviceProvider)
    {
        try
        {
            var sessionId = Guid.NewGuid().ToString("N")[..8];
            var logDir = Path.Combine("batch_processing_logs", $"session_{sessionId}");
            Directory.CreateDirectory(logDir);
            
            var logFile = Path.Combine(logDir, "batch_log.txt");
            var checkpointFile = Path.Combine(logDir, "checkpoint.json");
            
            // Initialize session log
            await File.WriteAllTextAsync(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] BATCH PROCESSING SESSION STARTED\n");
            await File.AppendAllTextAsync(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Session ID: {sessionId}\n");
            
            // Get initial statistics
            int totalDocuments, unextractedCount, extractedCount;
            using (var scope = serviceProvider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<JumpChainDbContext>();
                totalDocuments = await context.JumpDocuments.CountAsync();
                unextractedCount = await context.JumpDocuments.CountAsync(d => string.IsNullOrEmpty(d.ExtractedText));
                extractedCount = await context.JumpDocuments.CountAsync(d => !string.IsNullOrEmpty(d.ExtractedText));
            }
            
            var checkpoint = new
            {
                sessionId,
                startTime = DateTime.Now,
                totalDocuments,
                unextractedCount,
                extractedCount,
                currentBatch = 0,
                processedInSession = 0,
                successCount = 0,
                errorCount = 0,
                status = "initialized"
            };
            
            await File.WriteAllTextAsync(checkpointFile, JsonSerializer.Serialize(checkpoint, new JsonSerializerOptions { WriteIndented = true }));
            await File.AppendAllTextAsync(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Initial stats - Total: {totalDocuments}, Unextracted: {unextractedCount}, Extracted: {extractedCount}\n");
            
            return Results.Ok(new {
                success = true,
                sessionId,
                message = "Batch processing session initialized",
                logDirectory = logDir,
                initialStats = checkpoint
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

    private static async Task<IResult> ProcessBatch(
        IServiceProvider serviceProvider, 
        string sessionId, 
        int? batchSize = 50)
    {
        try
        {
            var logDir = Path.Combine("batch_processing_logs", $"session_{sessionId}");
            var logFile = Path.Combine(logDir, "batch_log.txt");
            var checkpointFile = Path.Combine(logDir, "checkpoint.json");
            
            if (!Directory.Exists(logDir))
            {
                return Results.BadRequest(new { success = false, error = "Session not found" });
            }
            
            // Load current checkpoint with null safety
            if (!File.Exists(checkpointFile))
            {
                return Results.BadRequest(new { success = false, error = "Checkpoint file not found" });
            }
            
            var checkpointJson = await File.ReadAllTextAsync(checkpointFile);
            var checkpoint = JsonSerializer.Deserialize<Dictionary<string, object>>(checkpointJson);
            
            if (checkpoint == null)
            {
                return Results.BadRequest(new { success = false, error = "Failed to parse checkpoint data" });
            }
            
            var currentBatch = Convert.ToInt32(checkpoint["currentBatch"]) + 1;
            var batchStartTime = DateTime.Now;
            
            await File.AppendAllTextAsync(logFile, $"\n[{batchStartTime:yyyy-MM-dd HH:mm:ss}] STARTING BATCH {currentBatch} (size: {batchSize})\n");
            
            int batchSuccessCount = 0;
            int batchErrorCount = 0;
            var batchResults = new List<object>();
            
            using (var scope = serviceProvider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<JumpChainDbContext>();
                var driveService = scope.ServiceProvider.GetRequiredService<IGoogleDriveService>();
                
                // Get next batch of unextracted documents
                var documentsToProcess = await context.JumpDocuments
                    .Where(d => string.IsNullOrEmpty(d.ExtractedText))
                    .OrderBy(d => d.Id)
                    .Take(batchSize ?? 50)
                    .ToListAsync();
                
                if (!documentsToProcess.Any())
                {
                    await File.AppendAllTextAsync(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] BATCH {currentBatch}: No more documents to process\n");
                    
                    // Update final checkpoint
                    checkpoint["status"] = "completed";
                    checkpoint["endTime"] = DateTime.Now;
                    await File.WriteAllTextAsync(checkpointFile, JsonSerializer.Serialize(checkpoint, new JsonSerializerOptions { WriteIndented = true }));
                    
                    return Results.Ok(new {
                        success = true,
                        completed = true,
                        message = "All documents processed!",
                        sessionId,
                        currentBatch,
                        finalStats = checkpoint
                    });
                }
                
                await File.AppendAllTextAsync(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] BATCH {currentBatch}: Processing {documentsToProcess.Count} documents\n");
                
                foreach (var document in documentsToProcess)
                {
                    var docStartTime = DateTime.Now;
                    try
                    {
                        await File.AppendAllTextAsync(logFile, $"[{docStartTime:yyyy-MM-dd HH:mm:ss}] Processing Doc {document.Id}: {document.Name ?? "Unnamed"}\n");
                        
                        var (extractedText, method) = await driveService.ExtractTextWithMethodAsync(document.GoogleDriveFileId);
                        
                        if (string.IsNullOrEmpty(extractedText))
                        {
                            await File.AppendAllTextAsync(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Doc {document.Id}: No text extracted\n");
                            batchErrorCount++;
                            batchResults.Add(new { docId = document.Id, status = "no_text", method = method ?? "None" });
                        }
                        else
                        {
                            document.ExtractedText = extractedText;
                            document.ExtractionMethod = method;
                            var textLength = extractedText.Length;
                            
                            await File.AppendAllTextAsync(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Doc {document.Id}: SUCCESS - {textLength} chars via {method ?? "Unknown"}\n");
                            batchSuccessCount++;
                            batchResults.Add(new { docId = document.Id, status = "success", method = method ?? "Unknown", textLength });
                        }
                    }
                    catch (Exception docEx)
                    {
                        await File.AppendAllTextAsync(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Doc {document.Id}: ERROR - {docEx.Message}\n");
                        batchErrorCount++;
                        batchResults.Add(new { docId = document.Id, status = "error", error = docEx.Message });
                    }
                }
                
                // Save all changes
                await context.SaveChangesAsync();
            }
            
            var batchEndTime = DateTime.Now;
            var batchDuration = batchEndTime - batchStartTime;
            
            await File.AppendAllTextAsync(logFile, $"[{batchEndTime:yyyy-MM-dd HH:mm:ss}] BATCH {currentBatch} COMPLETED\n");
            await File.AppendAllTextAsync(logFile, $"[{batchEndTime:yyyy-MM-dd HH:mm:ss}] Duration: {batchDuration.TotalMinutes:F1} minutes\n");
            await File.AppendAllTextAsync(logFile, $"[{batchEndTime:yyyy-MM-dd HH:mm:ss}] Success: {batchSuccessCount}, Errors: {batchErrorCount}\n");
            
            // Update checkpoint
            checkpoint["currentBatch"] = currentBatch;
            checkpoint["processedInSession"] = Convert.ToInt32(checkpoint["processedInSession"]) + batchSuccessCount + batchErrorCount;
            checkpoint["successCount"] = Convert.ToInt32(checkpoint["successCount"]) + batchSuccessCount;
            checkpoint["errorCount"] = Convert.ToInt32(checkpoint["errorCount"]) + batchErrorCount;
            checkpoint["lastBatchTime"] = batchEndTime;
            checkpoint["status"] = "processing";
            
            await File.WriteAllTextAsync(checkpointFile, JsonSerializer.Serialize(checkpoint, new JsonSerializerOptions { WriteIndented = true }));
            
            // Get updated statistics
            int remainingCount;
            using (var scope = serviceProvider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<JumpChainDbContext>();
                remainingCount = await context.JumpDocuments.CountAsync(d => string.IsNullOrEmpty(d.ExtractedText));
            }
            
            await File.AppendAllTextAsync(logFile, $"[{batchEndTime:yyyy-MM-dd HH:mm:ss}] Remaining unprocessed documents: {remainingCount}\n");
            
            return Results.Ok(new {
                success = true,
                sessionId,
                currentBatch,
                batchResults = new {
                    documentsProcessed = batchSuccessCount + batchErrorCount,
                    successCount = batchSuccessCount,
                    errorCount = batchErrorCount,
                    duration = batchDuration.TotalMinutes,
                    remainingDocuments = remainingCount
                },
                sessionStats = checkpoint,
                nextBatchReady = remainingCount > 0,
                results = batchResults.Take(10) // Show first 10 for brevity
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

    private static async Task<IResult> GetBatchStatus(string sessionId)
    {
        try
        {
            var logDir = Path.Combine("batch_processing_logs", $"session_{sessionId}");
            var checkpointFile = Path.Combine(logDir, "checkpoint.json");
            
            if (!File.Exists(checkpointFile))
            {
                return Results.NotFound(new { success = false, error = "Session not found" });
            }
            
            var checkpointJson = await File.ReadAllTextAsync(checkpointFile);
            var checkpoint = JsonSerializer.Deserialize<Dictionary<string, object>>(checkpointJson);
            
            if (checkpoint == null)
            {
                return Results.BadRequest(new { success = false, error = "Failed to parse checkpoint data" });
            }
            
            // Get recent log entries with null safety
            var logFile = Path.Combine(logDir, "batch_log.txt");
            var logLines = File.Exists(logFile) ? await File.ReadAllLinesAsync(logFile) : Array.Empty<string>();
            var recentLogs = logLines.TakeLast(20).ToArray();
            
            return Results.Ok(new {
                success = true,
                sessionId,
                checkpoint,
                recentLogs,
                logDirectory = logDir
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

    private static async Task<IResult> ListSessions()
    {
        try
        {
            var logDir = "batch_processing_logs";
            if (!Directory.Exists(logDir))
            {
                return Results.Ok(new { success = true, sessions = Array.Empty<object>() });
            }
            
            var sessionDirs = Directory.GetDirectories(logDir, "session_*");
            var sessions = new List<object>();
            
            foreach (var sessionDir in sessionDirs)
            {
                var sessionId = Path.GetFileName(sessionDir)?.Replace("session_", "") ?? "unknown";
                var checkpointFile = Path.Combine(sessionDir, "checkpoint.json");
                
                if (File.Exists(checkpointFile))
                {
                    try
                    {
                        var checkpointJson = await File.ReadAllTextAsync(checkpointFile);
                        var checkpoint = JsonSerializer.Deserialize<Dictionary<string, object>>(checkpointJson);
                        
                        if (checkpoint != null)
                        {
                            sessions.Add(new {
                                sessionId,
                                status = checkpoint.GetValueOrDefault("status", "unknown")?.ToString() ?? "unknown",
                                startTime = checkpoint.GetValueOrDefault("startTime"),
                                processedInSession = checkpoint.GetValueOrDefault("processedInSession", 0),
                                successCount = checkpoint.GetValueOrDefault("successCount", 0),
                                errorCount = checkpoint.GetValueOrDefault("errorCount", 0),
                                currentBatch = checkpoint.GetValueOrDefault("currentBatch", 0)
                            });
                        }
                    }
                    catch
                    {
                        // Skip malformed checkpoint files
                    }
                }
            }
            
            return Results.Ok(new {
                success = true,
                sessions = sessions.OrderByDescending(s => ((dynamic)s).startTime)
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

    private static async Task<IResult> ResumeSession(string sessionId)
    {
        try
        {
            var logDir = Path.Combine("batch_processing_logs", $"session_{sessionId}");
            var logFile = Path.Combine(logDir, "batch_log.txt");
            var checkpointFile = Path.Combine(logDir, "checkpoint.json");
            
            if (!File.Exists(checkpointFile))
            {
                return Results.NotFound(new { success = false, error = "Session not found" });
            }
            
            var checkpointJson = await File.ReadAllTextAsync(checkpointFile);
            var checkpoint = JsonSerializer.Deserialize<Dictionary<string, object>>(checkpointJson);
            
            if (checkpoint == null)
            {
                return Results.BadRequest(new { success = false, error = "Failed to parse checkpoint data" });
            }
            
            checkpoint["status"] = "resumed";
            checkpoint["resumeTime"] = DateTime.Now;
            
            await File.WriteAllTextAsync(checkpointFile, JsonSerializer.Serialize(checkpoint, new JsonSerializerOptions { WriteIndented = true }));
            
            if (File.Exists(logFile))
            {
                await File.AppendAllTextAsync(logFile, $"\n[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] SESSION RESUMED\n");
            }
            
            return Results.Ok(new {
                success = true,
                message = "Session resumed successfully",
                sessionId,
                checkpoint
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

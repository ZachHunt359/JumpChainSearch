using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using JumpChainSearch.Data;
using JumpChainSearch.Models;
using JumpChainSearch.Services;

namespace JumpChainSearch.Extensions;

public static class DatabaseManagementEndpoints
{
    public static RouteGroupBuilder MapDatabaseManagementEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/populate", PopulateDatabase);
        group.MapPost("/populate-simple", PopulateSimple);
        group.MapGet("/analyze-duplicates", AnalyzeDuplicates);
        group.MapPost("/merge-duplicates", MergeDuplicates);
        group.MapPost("/merge-duplicates/{groupIndex:int}", MergeDuplicates);
        group.MapPost("/cleanup-urls", CleanupDuplicateUrls);
        group.MapPost("/update-schema", UpdateDatabaseSchema);
        
        return group;
    }

    private static async Task<IResult> PopulateDatabase(
        IGoogleDriveService driveService, 
        JumpChainDbContext context)
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

            var totalDocuments = 0;
            var results = new List<object>();

            foreach (var drive in drives)
            {
                try
                {
                    var driveConfig = new DriveConfiguration { DriveId = drive.folderId, DriveName = drive.name };
                    var (documents, method) = await driveService.ScanDriveUnifiedAsync(driveConfig);
                    var documentList = documents.ToList();
                    
                    // Save to database with automatic deduplication
                    foreach (var doc in documentList)
                    {
                        // Check if document already exists by GoogleDriveFileId
                        var existing = await context.JumpDocuments
                            .Include(d => d.Tags)
                            .FirstOrDefaultAsync(d => d.GoogleDriveFileId == doc.GoogleDriveFileId);
                        
                        if (existing == null)
                        {
                            // Check for potential duplicate (same name, size, mime type)
                            var duplicate = await context.JumpDocuments
                                .Include(d => d.Tags)
                                .FirstOrDefaultAsync(d => 
                                    d.Name.ToLower().Trim() == doc.Name.ToLower().Trim() &&
                                    d.Size == doc.Size &&
                                    d.MimeType == doc.MimeType);
                            
                            if (duplicate != null)
                            {
                                // Found a duplicate - merge this document into the existing one
                                // Add the new file as an alternate URL
                                context.DocumentUrls.Add(new DocumentUrl
                                {
                                    JumpDocumentId = duplicate.Id,
                                    GoogleDriveFileId = doc.GoogleDriveFileId,
                                    SourceDrive = doc.SourceDrive,
                                    FolderPath = doc.FolderPath,
                                    WebViewLink = doc.WebViewLink,
                                    DownloadLink = doc.DownloadLink,
                                    LastScanned = DateTime.UtcNow
                                });
                                
                                // Merge tags from new document to existing
                                foreach (var tag in doc.Tags)
                                {
                                    var existingTag = duplicate.Tags
                                        .FirstOrDefault(t => t.TagName == tag.TagName && t.TagCategory == tag.TagCategory);
                                    
                                    if (existingTag == null)
                                    {
                                        duplicate.Tags.Add(new DocumentTag
                                        {
                                            JumpDocumentId = duplicate.Id,
                                            TagName = tag.TagName,
                                            TagCategory = tag.TagCategory
                                        });
                                    }
                                }
                                
                                duplicate.LastScanned = DateTime.UtcNow;
                            }
                            else
                            {
                                // No duplicate found - add as new document
                                context.JumpDocuments.Add(doc);
                            }
                        }
                        else
                        {
                            // Update existing document
                            existing.Name = doc.Name;
                            existing.Description = doc.Description;
                            existing.FolderPath = doc.FolderPath;
                            existing.Size = doc.Size;
                            existing.ModifiedTime = doc.ModifiedTime;
                            existing.LastScanned = DateTime.UtcNow;
                            
                            // Remove existing tags and add new ones
                            context.DocumentTags.RemoveRange(existing.Tags);
                            existing.Tags.Clear();
                            
                            foreach (var tag in doc.Tags)
                            {
                                existing.Tags.Add(new DocumentTag
                                {
                                    TagName = tag.TagName,
                                    TagCategory = tag.TagCategory,
                                    JumpDocument = existing
                                });
                            }
                        }
                    }
                    
                    await context.SaveChangesAsync();
                    totalDocuments += documentList.Count;
                    
                    results.Add(new { 
                        driveName = drive.name, 
                        documentCount = documentList.Count,
                        status = "success"
                    });
                }
                catch (Exception ex)
                {
                    results.Add(new { 
                        driveName = drive.name, 
                        documentCount = 0,
                        status = "error",
                        error = ex.Message,
                        innerError = ex.InnerException?.Message,
                        stackTrace = ex.StackTrace
                    });
                }
            }

            return Results.Ok(new {
                success = true,
                message = $"Database populated with {totalDocuments} documents",
                totalDocuments,
                driveResults = results
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

    private static async Task<IResult> PopulateSimple(
        IGoogleDriveService driveService, 
        JumpChainDbContext context)
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

            var totalDocuments = 0;
            var results = new List<object>();

            foreach (var drive in drives)
            {
                try
                {
                    var driveConfig = new DriveConfiguration { DriveId = drive.folderId, DriveName = drive.name };
                    var (documents, method) = await driveService.ScanDriveUnifiedAsync(driveConfig);
                    var documentList = documents.ToList();
                    
                    // Save to database without tags
                    foreach (var doc in documentList)
                    {
                        // Clear tags to avoid Entity Framework relationship issues
                        doc.Tags.Clear();
                        
                        // Check if document already exists
                        var existing = await context.JumpDocuments
                            .FirstOrDefaultAsync(d => d.GoogleDriveFileId == doc.GoogleDriveFileId);
                        
                        if (existing == null)
                        {
                            context.JumpDocuments.Add(doc);
                        }
                        else
                        {
                            // Update existing document
                            existing.Name = doc.Name;
                            existing.Description = doc.Description;
                            existing.FolderPath = doc.FolderPath;
                            existing.Size = doc.Size;
                            existing.ModifiedTime = doc.ModifiedTime;
                            existing.LastScanned = DateTime.UtcNow;
                        }
                    }
                    
                    await context.SaveChangesAsync();
                    totalDocuments += documentList.Count;
                    
                    results.Add(new { 
                        driveName = drive.name, 
                        documentCount = documentList.Count,
                        status = "success"
                    });
                }
                catch (Exception ex)
                {
                    results.Add(new { 
                        driveName = drive.name, 
                        documentCount = 0,
                        status = "error",
                        error = ex.Message,
                        innerError = ex.InnerException?.Message
                    });
                }
            }

            return Results.Ok(new {
                success = true,
                message = $"Database populated with {totalDocuments} documents (without tags)",
                totalDocuments,
                driveResults = results
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

    private static async Task<IResult> AnalyzeDuplicates(JumpChainDbContext context)
    {
        try
        {
            var allDocuments = await context.JumpDocuments.ToListAsync();
            
            var potentialDuplicates = allDocuments
                .GroupBy(d => new { 
                    NormalizedName = d.Name.ToLower().Trim(),
                    d.Size,
                    d.MimeType
                })
                .Where(g => g.Count() > 1)
                .Select(g => new {
                    Name = g.First().Name,
                    Size = g.Key.Size,
                    MimeType = g.Key.MimeType,
                    Count = g.Count(),
                    Documents = g.Select(d => new {
                        d.Id,
                        d.Name,
                        d.SourceDrive,
                        d.FolderPath,
                        d.GoogleDriveFileId
                    }).ToList()
                })
                .OrderByDescending(g => g.Count)
                .ToList();

            var duplicateStats = new
            {
                GroupCount = potentialDuplicates.Count,
                TotalDuplicateDocuments = potentialDuplicates.Sum(g => g.Count),
                DocumentsThatCouldBeRemoved = potentialDuplicates.Sum(g => g.Count - 1),
                LargestDuplicateGroup = potentialDuplicates.Any() ? potentialDuplicates.Max(g => g.Count) : 0
            };

            return Results.Ok(new {
                success = true,
                message = $"Found {duplicateStats.GroupCount} groups of potential duplicates affecting {duplicateStats.TotalDuplicateDocuments} documents",
                stats = duplicateStats,
                duplicateGroups = potentialDuplicates.Take(20) // Show first 20 groups for review
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

    private static async Task<IResult> MergeDuplicates(JumpChainDbContext context, IDocumentCountService documentCountService, int? groupIndex = null)
    {
        Console.WriteLine($"[MergeDuplicates] Called with groupIndex: {groupIndex}");
        try
        {
            // Find potential duplicates again (same logic as analyze)
            var allDocuments = await context.JumpDocuments
                .Include(d => d.Tags)
                .ToListAsync();
                
            var duplicateGroups = allDocuments
                .GroupBy(d => new { 
                    NormalizedName = d.Name.ToLower().Trim(),
                    d.Size,
                    d.MimeType
                })
                .Where(g => g.Count() > 1)
                .ToList();

            if (!duplicateGroups.Any())
            {
                return Results.Ok(new {
                    success = true,
                    message = "No duplicates found to merge",
                    mergedGroups = 0,
                    documentsMerged = 0
                });
            }

            int totalMergedGroups = 0;
            int totalDocumentsMerged = 0;
            var mergeResults = new List<object>();

            // Get ALL existing DocumentUrls globally to avoid UNIQUE constraint violations
            var allExistingUrls = await context.DocumentUrls.ToListAsync();
            var existingGoogleDriveFileIds = allExistingUrls.Select(u => u.GoogleDriveFileId).ToHashSet();

            // If specific group index provided, merge only that group
            var groupsToProcess = groupIndex.HasValue && groupIndex.Value < duplicateGroups.Count
                ? new[] { duplicateGroups[groupIndex.Value] }.ToList()
                : duplicateGroups;

            foreach (var group in groupsToProcess)
            {
                var documents = group.OrderBy(d => d.Id).ToList();
                var primaryDocument = documents.First(); // Keep the first document as primary
                var duplicates = documents.Skip(1).ToList();

                // Collect all unique URLs from duplicates (excluding primary document's URL)
                var urlsToAdd = new List<DocumentUrl>();
                
                // Add URLs from duplicate documents only (primary document keeps its URL in main properties)
                foreach (var duplicate in duplicates)
                {
                    // Skip if this GoogleDriveFileId already exists in DocumentUrls table
                    if (existingGoogleDriveFileIds.Contains(duplicate.GoogleDriveFileId))
                    {
                        continue;
                    }
                    
                    // Skip if this is the same GoogleDriveFileId as the primary document itself
                    if (duplicate.GoogleDriveFileId == primaryDocument.GoogleDriveFileId)
                    {
                        continue;
                    }

                    urlsToAdd.Add(new DocumentUrl
                    {
                        JumpDocumentId = primaryDocument.Id,
                        GoogleDriveFileId = duplicate.GoogleDriveFileId,
                        SourceDrive = duplicate.SourceDrive,
                        FolderPath = duplicate.FolderPath,
                        WebViewLink = duplicate.WebViewLink,
                        DownloadLink = duplicate.DownloadLink,
                        LastScanned = duplicate.LastScanned
                    });

                    // Merge tags from duplicates to primary document
                    foreach (var tag in duplicate.Tags)
                    {
                        // Check if primary document already has this tag
                        var existingTag = primaryDocument.Tags
                            .FirstOrDefault(t => t.TagName == tag.TagName && t.TagCategory == tag.TagCategory);
                        
                        if (existingTag == null)
                        {
                            // Add unique tag to primary document
                            primaryDocument.Tags.Add(new DocumentTag
                            {
                                JumpDocumentId = primaryDocument.Id,
                                TagName = tag.TagName,
                                TagCategory = tag.TagCategory
                            });
                        }
                    }
                }

                // Add all URLs to the primary document
                if (urlsToAdd.Any())
                {
                    context.DocumentUrls.AddRange(urlsToAdd);
                }

                // Remove duplicate documents (this will cascade delete their tags)
                context.JumpDocuments.RemoveRange(duplicates);

                mergeResults.Add(new {
                    primaryDocumentId = primaryDocument.Id,
                    primaryDocumentName = primaryDocument.Name,
                    duplicatesRemoved = duplicates.Count,
                    urlsAdded = urlsToAdd.Count,
                    uniqueSourceDrives = urlsToAdd.Select(u => u.SourceDrive).Distinct().Count()
                });

                totalMergedGroups++;
                totalDocumentsMerged += duplicates.Count;
            }

            // Save all changes
            await context.SaveChangesAsync();
            
            // Refresh document count after merge
            await documentCountService.RefreshCountAsync();
            var finalCount = await documentCountService.GetCountAsync();

            return Results.Ok(new {
                success = true,
                message = $"Successfully merged {totalMergedGroups} duplicate groups, consolidating {totalDocumentsMerged} documents",
                mergedGroups = totalMergedGroups,
                documentsMerged = totalDocumentsMerged,
                currentTotalDocuments = finalCount,
                mergeDetails = mergeResults
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

    private static async Task<IResult> CleanupDuplicateUrls(JumpChainDbContext context)
    {
        try
        {
            // Find all documents that have DocumentUrl records matching their primary URL
            var documentsWithUrls = await context.JumpDocuments
                .Include(d => d.Urls)
                .Where(d => d.Urls.Any())
                .ToListAsync();

            var duplicateUrls = new List<DocumentUrl>();
            
            foreach (var document in documentsWithUrls)
            {
                // Find DocumentUrl records that match the primary document's URL
                var matchingUrls = document.Urls
                    .Where(u => u.GoogleDriveFileId == document.GoogleDriveFileId &&
                               u.SourceDrive == document.SourceDrive &&
                               u.FolderPath == document.FolderPath)
                    .ToList();
                    
                duplicateUrls.AddRange(matchingUrls);
            }

            if (duplicateUrls.Any())
            {
                context.DocumentUrls.RemoveRange(duplicateUrls);
                await context.SaveChangesAsync();
                
                return Results.Ok(new {
                    success = true,
                    message = $"Removed {duplicateUrls.Count} duplicate URL records",
                    removedUrls = duplicateUrls.Count
                });
            }
            
            return Results.Ok(new {
                success = true,
                message = "No duplicate URL records found",
                removedUrls = 0
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

    private static async Task<IResult> UpdateDatabaseSchema(JumpChainDbContext context)
    {
        try
        {
            var connectionString = Environment.GetEnvironmentVariable("CONNECTION_STRING") 
                                   ?? "Data Source=jumpchain.db";
            
            using var connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionString);
            await connection.OpenAsync();
            
            var results = new List<string>();
            
            // Check if LastModified column exists
            var checkLastModifiedCmd = connection.CreateCommand();
            checkLastModifiedCmd.CommandText = "PRAGMA table_info(JumpDocuments)";
            var columns = new List<string>();
            
            using (var reader = await checkLastModifiedCmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    columns.Add(reader.GetString(1)); // Column name is at index 1 in PRAGMA table_info
                }
            }
            
            // Add LastModified column if missing
            if (!columns.Contains("LastModified"))
            {
                var addLastModifiedCmd = connection.CreateCommand();
                addLastModifiedCmd.CommandText = "ALTER TABLE JumpDocuments ADD COLUMN LastModified TEXT NOT NULL DEFAULT '1900-01-01T00:00:00.000Z'";
                await addLastModifiedCmd.ExecuteNonQueryAsync();
                results.Add("Added LastModified column");
            }
            else
            {
                results.Add("LastModified column already exists");
            }
            
            // Add ExtractionMethod column if missing
            if (!columns.Contains("ExtractionMethod"))
            {
                var addExtractionMethodCmd = connection.CreateCommand();
                addExtractionMethodCmd.CommandText = "ALTER TABLE JumpDocuments ADD COLUMN ExtractionMethod TEXT";
                await addExtractionMethodCmd.ExecuteNonQueryAsync();
                results.Add("Added ExtractionMethod column");
            }
            else
            {
                results.Add("ExtractionMethod column already exists");
            }
            
            await connection.CloseAsync();
            
            return Results.Ok(new {
                success = true,
                message = "Database schema updated successfully",
                changes = results,
                timestamp = DateTime.Now
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

    private record JumpChainDriveConfig(string name, string folderId);
}

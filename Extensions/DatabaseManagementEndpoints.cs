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
        JumpChainDbContext context,
        DriveScanCoordinator scanCoordinator)
    {
        if (!scanCoordinator.TryAcquire("database population scan", out var scanLease))
        {
            return Results.Conflict(new
            {
                success = false,
                error = "Another drive scan or folder refresh is already running.",
                activeOperation = scanCoordinator.ActiveOperation
            });
        }

        using var activeScan = scanLease;
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
        JumpChainDbContext context,
        DriveScanCoordinator scanCoordinator)
    {
        if (!scanCoordinator.TryAcquire("simple database population scan", out var scanLease))
        {
            return Results.Conflict(new
            {
                success = false,
                error = "Another drive scan or folder refresh is already running.",
                activeOperation = scanCoordinator.ActiveOperation
            });
        }

        using var activeScan = scanLease;
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

    private static async Task<IResult> MergeDuplicates(
        JumpChainDbContext context,
        IDocumentCountService documentCountService,
        IDocumentLinkHealthService healthService,
        int? groupIndex = null)
    {
        Console.WriteLine($"[MergeDuplicates] Called with groupIndex: {groupIndex}");
        await using var transaction = await context.Database.BeginTransactionAsync();
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
            var duplicateTagsToRemove = new List<DocumentTag>();
            var duplicateDocumentsToRemove = new List<JumpDocument>();

            // Get all existing URL IDs globally to avoid UNIQUE constraint violations.
            var existingGoogleDriveFileIds = (await context.DocumentUrls
                .Select(url => url.GoogleDriveFileId)
                .ToListAsync())
                .ToHashSet(StringComparer.Ordinal);

            // If specific group index provided, merge only that group
            var groupsToProcess = groupIndex.HasValue && groupIndex.Value < duplicateGroups.Count
                ? new[] { duplicateGroups[groupIndex.Value] }.ToList()
                : duplicateGroups;

            foreach (var group in groupsToProcess)
            {
                var documents = group.OrderBy(d => d.Id).ToList();
                var primaryDocument = documents.First(); // Keep the first document as primary
                var duplicates = documents.Skip(1).ToList();
                var duplicateIds = duplicates.Select(document => document.Id).ToList();
                var mergeStats = await ReparentDuplicateDependents(
                    context,
                    primaryDocument,
                    duplicates,
                    duplicateIds,
                    existingGoogleDriveFileIds);

                await context.Entry(primaryDocument)
                    .Collection(document => document.Urls)
                    .LoadAsync();
                await DocumentLinkEndpoints.VerifySourcesAsync(primaryDocument.Urls, healthService);
                DocumentLinkEndpoints.SynchronizeDocumentAvailability(primaryDocument);

                duplicateTagsToRemove.AddRange(mergeStats.TagsToRemove);
                duplicateDocumentsToRemove.AddRange(duplicates);

                mergeResults.Add(new {
                    primaryDocumentId = primaryDocument.Id,
                    primaryDocumentName = primaryDocument.Name,
                    duplicatesRemoved = duplicates.Count,
                    urlsAdded = mergeStats.UrlsAdded,
                    urlsReparented = mergeStats.UrlsReparented,
                    tagsReparented = mergeStats.TagsReparented,
                    duplicateTagsRemoved = mergeStats.DuplicateTagsRemoved,
                    relatedRowsReparented = mergeStats.RelatedRowsReparented
                });

                totalMergedGroups++;
                totalDocumentsMerged += duplicates.Count;
            }

            // Persist foreign-key reparenting before deleting referenced tags or documents.
            await context.SaveChangesAsync();

            context.DocumentTags.RemoveRange(duplicateTagsToRemove);
            await context.SaveChangesAsync();

            context.JumpDocuments.RemoveRange(duplicateDocumentsToRemove);
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
            
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
            await transaction.RollbackAsync();
            return Results.BadRequest(new { 
                success = false, 
                error = ex.Message,
                details = ex.InnerException?.Message
            });
        }
    }

    private static async Task<DuplicateMergeStats> ReparentDuplicateDependents(
        JumpChainDbContext context,
        JumpDocument primaryDocument,
        List<JumpDocument> duplicates,
        List<int> duplicateIds,
        HashSet<string> existingGoogleDriveFileIds)
    {
        var mergedDocumentIds = duplicateIds.Append(primaryDocument.Id).ToList();
        var urls = await context.DocumentUrls
            .Where(url => duplicateIds.Contains(url.JumpDocumentId))
            .ToListAsync();
        foreach (var url in urls)
            url.JumpDocumentId = primaryDocument.Id;

        var urlsAdded = 0;
        foreach (var duplicate in duplicates)
        {
            if (string.IsNullOrWhiteSpace(duplicate.GoogleDriveFileId) ||
                duplicate.GoogleDriveFileId == primaryDocument.GoogleDriveFileId ||
                !existingGoogleDriveFileIds.Add(duplicate.GoogleDriveFileId))
            {
                continue;
            }

            context.DocumentUrls.Add(new DocumentUrl
            {
                JumpDocumentId = primaryDocument.Id,
                GoogleDriveFileId = duplicate.GoogleDriveFileId,
                SourceDrive = duplicate.SourceDrive,
                FolderPath = duplicate.FolderPath,
                WebViewLink = duplicate.WebViewLink,
                DownloadLink = duplicate.DownloadLink,
                LastScanned = duplicate.LastScanned
            });
            urlsAdded++;
        }

        var duplicateTagIds = duplicates
            .SelectMany(document => document.Tags)
            .Select(tag => tag.Id)
            .ToList();
        var removalRequests = await context.TagRemovalRequests
            .Where(request => duplicateIds.Contains(request.JumpDocumentId) ||
                              (request.DocumentTagId.HasValue && duplicateTagIds.Contains(request.DocumentTagId.Value)))
            .ToListAsync();
        var primaryTags = primaryDocument.Tags.ToList();
        var tagsToRemove = new List<DocumentTag>();
        var tagsReparented = 0;
        var duplicateTagsRemoved = 0;

        foreach (var duplicate in duplicates)
        {
            foreach (var duplicateTag in duplicate.Tags.ToList())
            {
                var primaryTag = primaryTags.FirstOrDefault(tag =>
                    tag.TagName.Equals(duplicateTag.TagName, StringComparison.OrdinalIgnoreCase));
                var referencingRequests = removalRequests
                    .Where(request => request.DocumentTagId == duplicateTag.Id)
                    .ToList();

                if (primaryTag != null)
                {
                    foreach (var request in referencingRequests)
                        request.DocumentTagId = primaryTag.Id;
                    tagsToRemove.Add(duplicateTag);
                    duplicateTagsRemoved++;
                }
                else
                {
                    duplicateTag.JumpDocumentId = primaryDocument.Id;
                    duplicateTag.JumpDocument = primaryDocument;
                    primaryTags.Add(duplicateTag);
                    tagsReparented++;
                }
            }
        }

        foreach (var request in removalRequests)
            request.JumpDocumentId = primaryDocument.Id;

        var suggestions = await context.TagSuggestions
            .Where(suggestion => duplicateIds.Contains(suggestion.JumpDocumentId))
            .ToListAsync();
        foreach (var suggestion in suggestions)
            suggestion.JumpDocumentId = primaryDocument.Id;

        var purchasables = await context.DocumentPurchasables
            .Where(purchasable => duplicateIds.Contains(purchasable.JumpDocumentId))
            .ToListAsync();
        foreach (var purchasable in purchasables)
            purchasable.JumpDocumentId = primaryDocument.Id;

        var overrides = await context.UserTagOverrides
            .Where(tagOverride => mergedDocumentIds.Contains(tagOverride.JumpDocumentId))
            .ToListAsync();
        var duplicateOverrides = overrides
            .GroupBy(tagOverride => new
            {
                tagOverride.UserId,
                TagName = tagOverride.TagName.ToUpperInvariant(),
                TagCategory = tagOverride.TagCategory.ToUpperInvariant()
            })
            .SelectMany(group => group
                .OrderByDescending(tagOverride => tagOverride.CreatedAt)
                .ThenByDescending(tagOverride => tagOverride.Id)
                .Skip(1))
            .ToHashSet();
        context.UserTagOverrides.RemoveRange(duplicateOverrides);
        foreach (var tagOverride in overrides.Where(tagOverride => !duplicateOverrides.Contains(tagOverride)))
            tagOverride.JumpDocumentId = primaryDocument.Id;

        var viewCounts = await context.DocumentViewCounts
            .Where(view => mergedDocumentIds.Contains(view.JumpDocumentId))
            .ToListAsync();
        if (viewCounts.Count > 0)
        {
            var primaryView = viewCounts.FirstOrDefault(view => view.JumpDocumentId == primaryDocument.Id)
                ?? viewCounts.OrderByDescending(view => view.LastViewed).First();
            primaryView.JumpDocumentId = primaryDocument.Id;
            primaryView.ViewCount = viewCounts.Sum(view => view.ViewCount);
            primaryView.UniqueViewCount = viewCounts.Sum(view => view.UniqueViewCount);
            primaryView.LastViewed = viewCounts.Max(view => view.LastViewed);
            context.DocumentViewCounts.RemoveRange(viewCounts.Where(view => view != primaryView));
        }

        return new DuplicateMergeStats(
            urlsAdded,
            urls.Count,
            tagsReparented,
            duplicateTagsRemoved,
            removalRequests.Count + suggestions.Count + purchasables.Count + overrides.Count + viewCounts.Count,
            tagsToRemove);
    }

    private sealed record DuplicateMergeStats(
        int UrlsAdded,
        int UrlsReparented,
        int TagsReparented,
        int DuplicateTagsRemoved,
        int RelatedRowsReparented,
        IReadOnlyList<DocumentTag> TagsToRemove);

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

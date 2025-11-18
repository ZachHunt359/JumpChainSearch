using Microsoft.EntityFrameworkCore;
using JumpChainSearch.Data;
using JumpChainSearch.Models;
using JumpChainSearch.Helpers;

namespace JumpChainSearch.Extensions;

public static class TagManagementEndpoints
{
    public static RouteGroupBuilder MapTagManagementEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/debug-inconsistencies", DebugTagInconsistencies);
        group.MapPost("/fix-inconsistencies", FixTagInconsistencies);
        group.MapGet("/", GetTags);
        group.MapGet("/test-add", TestAddTags);
        group.MapPost("/refresh", RefreshTags);
        group.MapGet("/batch-refresh/{batchNumber:int?}", BatchRefreshTags);
        group.MapPost("/complete-all", CompleteAllTags);
        group.MapGet("/debug-untagged/{limit:int?}", DebugUntagged);
        group.MapGet("/methodical-test", MethodicalTagTest);
        group.MapGet("/test-one/{documentId:int}", TestTagOneDocument);
        group.MapGet("/methodical-fresh-context", MethodicalFreshContextTest);
        group.MapGet("/test-comprehensive-save/{documentId:int}", TestComprehensiveSave);
        group.MapGet("/debug-comprehensive/{documentId:int}", DebugComprehensiveTagging);
        group.MapPost("/batch-simple", BatchSimpleTagging);
        group.MapPost("/batch-comprehensive", BatchComprehensiveTagging);
        group.MapPost("/remove-sfw", RemoveSfwTags);
        group.MapPost("/remove-folder-tags", RemoveFolderTags);
        group.MapPost("/regenerate-extraction-tags", RegenerateExtractionTags);
        group.MapPost("/fix-naruto-tags", FixNarutoTags);
        group.MapPost("/regenerate-franchise-tags", RegenerateFranchiseTags);
        group.MapPost("/regenerate-genre-tags", RegenerateGenreTags);
        
        return group;
    }
    
    private static async Task<IResult> DebugTagInconsistencies(JumpChainDbContext context)
    {
        try
        {
            var inconsistentDocs = await context.JumpDocuments
                .Include(d => d.Tags)
                .Where(d => d.Tags.Count(t => t.TagCategory == "Format") > 1)
                .Select(d => new {
                    id = d.Id,
                    name = d.Name,
                    mimeType = d.MimeType,
                    formatTags = d.Tags.Where(t => t.TagCategory == "Format").Select(t => t.TagName).ToList(),
                    allTags = d.Tags.Select(t => t.TagName).ToList()
                })
                .Take(20)
                .ToListAsync();

            return Results.Ok(new {
                success = true,
                message = "Found documents with multiple format tags",
                inconsistentDocuments = inconsistentDocs,
                count = inconsistentDocs.Count
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
    
    private static async Task<IResult> FixTagInconsistencies(JumpChainDbContext context)
    {
        try
        {
            var inconsistentDocs = await context.JumpDocuments
                .Include(d => d.Tags)
                .Where(d => d.Tags.Count(t => t.TagCategory == "Format") > 1)
                .ToListAsync();

            int fixedCount = 0;
            foreach (var doc in inconsistentDocs)
            {
                // Remove all existing format tags
                var formatTags = doc.Tags.Where(t => t.TagCategory == "Format").ToList();
                foreach (var tag in formatTags)
                {
                    context.DocumentTags.Remove(tag);
                }

                // Add the correct format tag based on mime type
                var correctFormatTag = (doc.MimeType ?? "") switch
                {
                    "application/pdf" => "PDF",
                    "application/vnd.google-apps.document" => "Google Doc",
                    "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => "Word Doc",
                    "text/plain" => "Text",
                    _ when (doc.Name ?? "").ToLowerInvariant().EndsWith(".rtf") => "RTF",
                    _ when (doc.Name ?? "").ToLowerInvariant().EndsWith(".odt") => "OpenDocument",
                    _ => "Document"
                };

                doc.Tags.Add(new DocumentTag 
                { 
                    TagName = correctFormatTag, 
                    TagCategory = "Format",
                    JumpDocumentId = doc.Id
                });

                fixedCount++;
            }

            await context.SaveChangesAsync();

            return Results.Ok(new {
                success = true,
                message = $"Fixed {fixedCount} documents with tag inconsistencies",
                fixedDocuments = fixedCount
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
    
    private static async Task<IResult> GetTags(JumpChainDbContext context)
    {
        try
        {
            var tagFrequencies = await context.DocumentTags
                .GroupBy(t => new { t.TagName, t.TagCategory })
                .Select(g => new {
                    tagName = g.Key.TagName,
                    tagCategory = g.Key.TagCategory,
                    count = g.Count()
                })
                .OrderByDescending(t => t.count)
                .ThenBy(t => t.tagCategory)
                .ThenBy(t => t.tagName)
                .ToListAsync();

            var categorizedTags = tagFrequencies
                .GroupBy(t => t.tagCategory ?? "Unknown")
                .Where(g => g.Key != null)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(t => new { name = t.tagName, count = t.count }).ToList()
                );

            return Results.Ok(new {
                success = true,
                totalTags = tagFrequencies.Count,
                categorizedTags,
                allTags = tagFrequencies
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
    
    private static async Task<IResult> TestAddTags(JumpChainDbContext context)
    {
        try
        {
            var documents = await context.JumpDocuments.Take(10).ToListAsync();
            
            if (!documents.Any())
            {
                return Results.Ok(new { success = false, message = "No documents found" });
            }

            var tagsAdded = 0;
            foreach (var doc in documents)
            {
                var existingTags = await context.DocumentTags
                    .Where(t => t.JumpDocumentId == doc.Id)
                    .CountAsync();
                    
                if (existingTags == 0)
                {
                    var sampleTags = new List<DocumentTag>
                    {
                        new DocumentTag { TagName = doc.SourceDrive ?? "Unknown", TagCategory = "Drive", JumpDocumentId = doc.Id },
                        new DocumentTag { TagName = "JumpDoc", TagCategory = "ContentType", JumpDocumentId = doc.Id },
                        new DocumentTag { TagName = "PDF", TagCategory = "Format", JumpDocumentId = doc.Id },
                        new DocumentTag { TagName = "Medium", TagCategory = "Size", JumpDocumentId = doc.Id }
                    };

                    context.DocumentTags.AddRange(sampleTags);
                    tagsAdded += sampleTags.Count;
                }
            }

            if (tagsAdded > 0)
            {
                await context.SaveChangesAsync();
            }

            return Results.Ok(new { 
                success = true, 
                message = $"Added {tagsAdded} sample tags to test the UI",
                documentsProcessed = documents.Count
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
    
    private static Task<IResult> RefreshTags(JumpChainDbContext context)
    {
        // This endpoint appears to be a stub or deprecated
        return Task.FromResult(Results.Ok(new { 
            success = true, 
            message = "Use /batch-refresh/{batchNumber} for batch tag refresh" 
        }));
    }
    
    private static async Task<IResult> BatchRefreshTags(JumpChainDbContext context, int? batchNumber = 0)
    {
        try
        {
            int batchSize = 100;
            int skipCount = (batchNumber ?? 0) * batchSize;
            
            var totalDocuments = await context.JumpDocuments.CountAsync();
            var untaggedCount = await context.JumpDocuments
                .Where(d => !context.DocumentTags.Any(t => t.JumpDocumentId == d.Id))
                .CountAsync();
            
            var documents = await context.JumpDocuments
                .Where(d => !context.DocumentTags.Any(t => t.JumpDocumentId == d.Id))
                .Skip(skipCount)
                .Take(batchSize)
                .ToListAsync();
            
            if (!documents.Any())
            {
                return Results.Ok(new { 
                    success = true, 
                    message = "All documents have been tagged!",
                    totalDocuments,
                    untaggedRemaining = 0,
                    batchNumber = batchNumber ?? 0,
                    completed = true
                });
            }

            int processedCount = 0;
            foreach (var document in documents)
            {
                try
                {
                    var newTags = new List<DocumentTag>();

                    newTags.Add(new DocumentTag { TagName = document.SourceDrive ?? "Unknown", TagCategory = "Drive", JumpDocumentId = document.Id });

                    string contentType = DetermineContentType(document.Name ?? "", document.FolderPath ?? "");
                    newTags.Add(new DocumentTag { TagName = contentType, TagCategory = "ContentType", JumpDocumentId = document.Id });

                    string fileFormat = DetermineFileFormat(document.MimeType ?? "", document.Name ?? "");
                    if (!string.IsNullOrEmpty(fileFormat))
                    {
                        newTags.Add(new DocumentTag { TagName = fileFormat, TagCategory = "Format", JumpDocumentId = document.Id });
                    }

                    string sizeCategory = DetermineSizeCategory(document.Size);
                    newTags.Add(new DocumentTag { TagName = sizeCategory, TagCategory = "Size", JumpDocumentId = document.Id });

                    AddQualityTags(newTags, document.Name ?? "", document.FolderPath ?? "", document.Id);
                    AddSeriesTags(newTags, document.Name ?? "", document.FolderPath ?? "", document.Id);
                    TagGenerationHelpers.AddTextExtractionTag(newTags, document.ExtractedText, document.Id);

                    var existingTagNames = await context.DocumentTags
                        .Where(t => t.JumpDocumentId == document.Id)
                        .Select(t => t.TagName)
                        .ToListAsync();
                    
                    var tagsToAdd = newTags.Where(nt => !existingTagNames.Contains(nt.TagName)).ToList();
                    
                    if (tagsToAdd.Any())
                    {
                        try
                        {
                            context.DocumentTags.AddRange(tagsToAdd);
                            await context.SaveChangesAsync();
                            processedCount++;
                        }
                        catch (Exception tagEx)
                        {
                            Console.WriteLine($"Error adding tags to document {document.Id}: {tagEx.Message}");
                            context.ChangeTracker.Clear();
                        }
                    }
                }
                catch (Exception docEx)
                {
                    Console.WriteLine($"Error processing document {document.Id}: {docEx.Message}");
                }
            }

            var remainingUntagged = await context.JumpDocuments
                .Where(d => !context.DocumentTags.Any(t => t.JumpDocumentId == d.Id))
                .CountAsync();

            return Results.Ok(new { 
                success = true, 
                message = $"Processed batch {batchNumber ?? 0}: Tagged {processedCount} documents",
                totalDocuments,
                untaggedRemaining = remainingUntagged,
                batchNumber = batchNumber ?? 0,
                nextBatchNumber = (batchNumber ?? 0) + 1,
                documentsProcessed = processedCount,
                completed = remainingUntagged == 0
            });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { 
                success = false, 
                error = ex.Message,
                batchNumber = batchNumber ?? 0
            });
        }
    }
    
    private static async Task<IResult> CompleteAllTags(JumpChainDbContext context)
    {
        try
        {
            var totalDocuments = await context.JumpDocuments.CountAsync();
            var initialUntaggedCount = await context.JumpDocuments
                .Where(d => !d.Tags.Any())
                .CountAsync();
                
            if (initialUntaggedCount == 0)
            {
                return Results.Ok(new {
                    success = true,
                    message = "All documents already have tags!",
                    totalDocuments,
                    untaggedRemaining = 0,
                    completed = true
                });
            }

            int batchSize = 500;
            int totalProcessed = 0;
            int batchNumber = 0;

            while (true)
            {
                var untaggedDocuments = await context.JumpDocuments
                    .Where(d => !d.Tags.Any())
                    .Take(batchSize)
                    .ToListAsync();

                if (!untaggedDocuments.Any())
                {
                    break;
                }

                int batchProcessed = 0;
                foreach (var document in untaggedDocuments)
                {
                    try
                    {
                        var newTags = new List<DocumentTag>();

                        newTags.Add(new DocumentTag { TagName = document.SourceDrive ?? "Unknown", TagCategory = "Drive", JumpDocumentId = document.Id });

                        string contentType = DetermineContentType(document.Name ?? "", document.FolderPath ?? "");
                        newTags.Add(new DocumentTag { TagName = contentType, TagCategory = "ContentType", JumpDocumentId = document.Id });

                        string fileFormat = DetermineFileFormat(document.MimeType ?? "", document.Name ?? "");
                        if (!string.IsNullOrEmpty(fileFormat))
                        {
                            newTags.Add(new DocumentTag { TagName = fileFormat, TagCategory = "Format", JumpDocumentId = document.Id });
                        }

                        string sizeCategory = DetermineSizeCategory(document.Size);
                        newTags.Add(new DocumentTag { TagName = sizeCategory, TagCategory = "Size", JumpDocumentId = document.Id });

                        AddQualityTags(newTags, document.Name ?? "", document.FolderPath ?? "", document.Id);
                        AddSeriesTags(newTags, document.Name ?? "", document.FolderPath ?? "", document.Id);
                        TagGenerationHelpers.AddTextExtractionTag(newTags, document.ExtractedText, document.Id);

                        context.DocumentTags.AddRange(newTags);
                        batchProcessed++;
                    }
                    catch (Exception docEx)
                    {
                        Console.WriteLine($"Error processing document {document.Id}: {docEx.Message}");
                    }
                }

                if (batchProcessed > 0)
                {
                    try
                    {
                        await context.SaveChangesAsync();
                        totalProcessed += batchProcessed;
                        batchNumber++;
                        
                        Console.WriteLine($"Completed batch {batchNumber}: {batchProcessed} documents tagged. Total: {totalProcessed}");
                    }
                    catch (Exception saveEx)
                    {
                        Console.WriteLine($"Error saving batch {batchNumber}: {saveEx.Message}");
                    }
                }

                await Task.Delay(100);
            }

            var finalUntaggedCount = await context.JumpDocuments
                .Where(d => !d.Tags.Any())
                .CountAsync();

            return Results.Ok(new {
                success = true,
                message = $"Tag completion process finished! Processed {totalProcessed} documents in {batchNumber} batches.",
                totalDocuments,
                initialUntaggedCount,
                finalUntaggedCount,
                documentsProcessed = totalProcessed,
                batchesCompleted = batchNumber,
                completed = finalUntaggedCount == 0
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
    
    private static async Task<IResult> DebugUntagged(JumpChainDbContext context, int? limit = 10)
    {
        try
        {
            var totalDocs = await context.JumpDocuments.CountAsync();
            
            var docsWithTags = await context.JumpDocuments
                .Where(d => context.DocumentTags.Any(t => t.JumpDocumentId == d.Id))
                .CountAsync();
            
            var docsWithoutTags = await context.JumpDocuments
                .Where(d => !context.DocumentTags.Any(t => t.JumpDocumentId == d.Id))
                .CountAsync();
                
            var sampleUntagged = await context.JumpDocuments
                .Where(d => !context.DocumentTags.Any(t => t.JumpDocumentId == d.Id))
                .Take(limit ?? 10)
                .Select(d => new { d.Id, d.Name, d.SourceDrive })
                .ToListAsync();
            
            return Results.Ok(new {
                success = true,
                totalDocuments = totalDocs,
                documentsWithTags = docsWithTags,
                documentsWithoutTags = docsWithoutTags,
                sampleUntaggedDocuments = sampleUntagged
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
    
    private static async Task<IResult> MethodicalTagTest(JumpChainDbContext context)
    {
        try
        {
            var results = new List<object>();
            
            var initialUntaggedCount = await context.JumpDocuments
                .CountAsync(d => !context.DocumentTags.Any(t => t.JumpDocumentId == d.Id));
            
            results.Add(new { step = "initial", untaggedCount = initialUntaggedCount });

            for (int i = 0; i < 20; i++)
            {
                var untaggedDocument = await context.JumpDocuments
                    .FirstOrDefaultAsync(d => !context.DocumentTags.Any(t => t.JumpDocumentId == d.Id));
                
                if (untaggedDocument == null)
                {
                    results.Add(new { step = i + 1, message = "No more untagged documents found", document = (object?)null, tagsAdded = 0, verified = false });
                    break;
                }

                var newTags = new List<DocumentTag>();

                newTags.Add(new DocumentTag { TagName = untaggedDocument.SourceDrive ?? "Unknown", TagCategory = "Drive", JumpDocumentId = untaggedDocument.Id });

                string contentType = DetermineContentType(untaggedDocument.Name ?? "", untaggedDocument.FolderPath ?? "");
                newTags.Add(new DocumentTag { TagName = contentType, TagCategory = "ContentType", JumpDocumentId = untaggedDocument.Id });

                string fileFormat = DetermineFileFormat(untaggedDocument.MimeType ?? "", untaggedDocument.Name ?? "");
                if (!string.IsNullOrEmpty(fileFormat))
                {
                    newTags.Add(new DocumentTag { TagName = fileFormat, TagCategory = "Format", JumpDocumentId = untaggedDocument.Id });
                }

                string sizeCategory = DetermineSizeCategory(untaggedDocument.Size);
                newTags.Add(new DocumentTag { TagName = sizeCategory, TagCategory = "Size", JumpDocumentId = untaggedDocument.Id });

                AddQualityTags(newTags, untaggedDocument.Name ?? "", untaggedDocument.FolderPath ?? "", untaggedDocument.Id);
                AddSeriesTags(newTags, untaggedDocument.Name ?? "", untaggedDocument.FolderPath ?? "", untaggedDocument.Id);
                TagGenerationHelpers.AddTextExtractionTag(newTags, untaggedDocument.ExtractedText, untaggedDocument.Id);

                var existingTagCount = await context.DocumentTags
                    .CountAsync(t => t.JumpDocumentId == untaggedDocument.Id);
                    
                if (existingTagCount > 0)
                {
                    results.Add(new { 
                        step = i + 1,
                        document = new { 
                            id = untaggedDocument.Id, 
                            name = untaggedDocument.Name 
                        },
                        message = "Document already has tags, skipping",
                        tagsAdded = 0,
                        verified = false
                    });
                    continue;
                }

                context.DocumentTags.AddRange(newTags);
                
                try
                {
                    await context.SaveChangesAsync();
                    
                    var verificationTags = await context.DocumentTags
                        .Where(t => t.JumpDocumentId == untaggedDocument.Id)
                        .Select(t => new { t.TagName, t.TagCategory })
                        .ToListAsync();

                    var currentUntaggedCount = await context.JumpDocuments
                        .CountAsync(d => !context.DocumentTags.Any(t => t.JumpDocumentId == d.Id));

                    results.Add(new { 
                        step = i + 1,
                        document = new { 
                            id = untaggedDocument.Id, 
                            name = untaggedDocument.Name, 
                            folderPath = untaggedDocument.FolderPath,
                            sourceDrive = untaggedDocument.SourceDrive
                        },
                        tagsAdded = newTags.Count,
                        tagsVerified = verificationTags.Count,
                        tagDetails = verificationTags,
                        verified = verificationTags.Count == newTags.Count,
                        untaggedCountAfter = currentUntaggedCount
                    });
                }
                catch (Exception saveEx)
                {
                    context.ChangeTracker.Clear();
                    
                    results.Add(new { 
                        step = i + 1,
                        document = new { 
                            id = untaggedDocument.Id, 
                            name = untaggedDocument.Name 
                        },
                        error = saveEx.Message,
                        tagsAdded = 0,
                        verified = false
                    });
                }
            }

            var finalUntaggedCount = await context.JumpDocuments
                .CountAsync(d => !context.DocumentTags.Any(t => t.JumpDocumentId == d.Id));

            return Results.Ok(new {
                success = true,
                message = "Methodical tagging test completed",
                initialUntaggedCount,
                finalUntaggedCount,
                documentsProcessed = results.Count - 1,
                results
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
    
    private static async Task<IResult> TestTagOneDocument(JumpChainDbContext context, int documentId)
    {
        try
        {
            var document = await context.JumpDocuments
                .FirstOrDefaultAsync(d => d.Id == documentId);
                
            if (document == null)
            {
                return Results.NotFound(new { success = false, message = $"Document {documentId} not found" });
            }

            var existingTags = await context.DocumentTags
                .Where(t => t.JumpDocumentId == documentId)
                .Select(t => new { t.TagName, t.TagCategory })
                .ToListAsync();

            if (existingTags.Any())
            {
                return Results.Ok(new {
                    success = true,
                    message = "Document already has tags",
                    document = new { document.Id, document.Name },
                    existingTags = existingTags
                });
            }

            var newTags = new List<DocumentTag>
            {
                new DocumentTag { TagName = document.SourceDrive ?? "Unknown", TagCategory = "Drive", JumpDocumentId = document.Id },
                new DocumentTag { TagName = "PDF", TagCategory = "Format", JumpDocumentId = document.Id }
            };

            context.DocumentTags.AddRange(newTags);
            await context.SaveChangesAsync();

            var verifiedTags = await context.DocumentTags
                .Where(t => t.JumpDocumentId == documentId)
                .Select(t => new { t.TagName, t.TagCategory })
                .ToListAsync();

            return Results.Ok(new {
                success = true,
                message = "Tags added successfully",
                document = new { document.Id, document.Name, document.SourceDrive },
                tagsAdded = newTags.Count,
                verifiedTags = verifiedTags
            });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { 
                success = false, 
                error = ex.Message,
                details = ex.InnerException?.Message,
                documentId = documentId
            });
        }
    }
    
    private static async Task<IResult> MethodicalFreshContextTest(IServiceProvider serviceProvider)
    {
        try
        {
            var results = new List<object>();
            
            int initialUntaggedCount;
            using (var scope = serviceProvider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<JumpChainDbContext>();
                initialUntaggedCount = await context.JumpDocuments
                    .CountAsync(d => !context.DocumentTags.Any(t => t.JumpDocumentId == d.Id));
            }
            
            results.Add(new { step = "initial", untaggedCount = initialUntaggedCount });

            for (int i = 0; i < 20; i++)
            {
                using var scope = serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<JumpChainDbContext>();
                
                var untaggedDocument = await context.JumpDocuments
                    .FirstOrDefaultAsync(d => !context.DocumentTags.Any(t => t.JumpDocumentId == d.Id));
                
                if (untaggedDocument == null)
                {
                    results.Add(new { step = i + 1, message = "No more untagged documents found", document = (object?)null, tagsAdded = 0, verified = false });
                    break;
                }

                var existingTagCount = await context.DocumentTags
                    .CountAsync(t => t.JumpDocumentId == untaggedDocument.Id);
                    
                if (existingTagCount > 0)
                {
                    results.Add(new { 
                        step = i + 1,
                        document = new { 
                            id = untaggedDocument.Id, 
                            name = untaggedDocument.Name 
                        },
                        message = "Document already has tags, skipping",
                        tagsAdded = 0,
                        verified = false
                    });
                    continue;
                }

                var newTags = new List<DocumentTag>
                {
                    new DocumentTag { TagName = untaggedDocument.SourceDrive ?? "Unknown", TagCategory = "Drive", JumpDocumentId = untaggedDocument.Id },
                    new DocumentTag { TagName = "PDF", TagCategory = "Format", JumpDocumentId = untaggedDocument.Id }
                };

                context.DocumentTags.AddRange(newTags);
                
                try
                {
                    await context.SaveChangesAsync();
                    
                    var verificationTags = await context.DocumentTags
                        .Where(t => t.JumpDocumentId == untaggedDocument.Id)
                        .Select(t => new { t.TagName, t.TagCategory })
                        .ToListAsync();

                    int currentUntaggedCount;
                    using (var countScope = serviceProvider.CreateScope())
                    {
                        var countContext = countScope.ServiceProvider.GetRequiredService<JumpChainDbContext>();
                        currentUntaggedCount = await countContext.JumpDocuments
                            .CountAsync(d => !countContext.DocumentTags.Any(t => t.JumpDocumentId == d.Id));
                    }

                    results.Add(new { 
                        step = i + 1,
                        document = new { 
                            id = untaggedDocument.Id, 
                            name = untaggedDocument.Name, 
                            folderPath = untaggedDocument.FolderPath,
                            sourceDrive = untaggedDocument.SourceDrive
                        },
                        tagsAdded = newTags.Count,
                        tagsVerified = verificationTags.Count,
                        tagDetails = verificationTags,
                        verified = verificationTags.Count == newTags.Count,
                        untaggedCountAfter = currentUntaggedCount
                    });
                }
                catch (Exception saveEx)
                {                
                    results.Add(new { 
                        step = i + 1,
                        document = new { 
                            id = untaggedDocument.Id, 
                            name = untaggedDocument.Name 
                        },
                        error = saveEx.Message,
                        tagsAdded = 0,
                        verified = false
                    });
                }
            }

            int finalUntaggedCount;
            using (var scope = serviceProvider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<JumpChainDbContext>();
                finalUntaggedCount = await context.JumpDocuments
                    .CountAsync(d => !context.DocumentTags.Any(t => t.JumpDocumentId == d.Id));
            }

            return Results.Ok(new {
                success = true,
                message = "Methodical tagging test with fresh contexts and SIMPLE tagging completed",
                initialUntaggedCount,
                finalUntaggedCount,
                documentsProcessed = results.Count - 1,
                results
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
    
    private static async Task<IResult> TestComprehensiveSave(JumpChainDbContext context, int documentId)
    {
        try
        {
            var document = await context.JumpDocuments
                .FirstOrDefaultAsync(d => d.Id == documentId);
                
            if (document == null)
            {
                return Results.NotFound(new { success = false, message = $"Document {documentId} not found" });
            }

            var existingTags = await context.DocumentTags
                .Where(t => t.JumpDocumentId == documentId)
                .Select(t => new { t.TagName, t.TagCategory })
                .ToListAsync();

            var newTags = new List<DocumentTag>();

            newTags.Add(new DocumentTag { TagName = document.SourceDrive ?? "Unknown", TagCategory = "Drive", JumpDocumentId = document.Id });

            string contentType = DetermineContentType(document.Name ?? "", document.FolderPath ?? "");
            newTags.Add(new DocumentTag { TagName = contentType, TagCategory = "ContentType", JumpDocumentId = document.Id });

            string fileFormat = DetermineFileFormat(document.MimeType ?? "", document.Name ?? "");
            if (!string.IsNullOrEmpty(fileFormat))
            {
                newTags.Add(new DocumentTag { TagName = fileFormat, TagCategory = "Format", JumpDocumentId = document.Id });
            }

            string sizeCategory = DetermineSizeCategory(document.Size);
            newTags.Add(new DocumentTag { TagName = sizeCategory, TagCategory = "Size", JumpDocumentId = document.Id });

            AddQualityTags(newTags, document.Name ?? "", document.FolderPath ?? "", document.Id);
            AddSeriesTags(newTags, document.Name ?? "", document.FolderPath ?? "", document.Id);
            TagGenerationHelpers.AddTextExtractionTag(newTags, document.ExtractedText, document.Id);

            var existingTagNames = existingTags.Select(t => t.TagName).ToHashSet();
            
            var uniqueNewTags = newTags
                .GroupBy(t => new { t.TagName, t.TagCategory })
                .Select(g => g.First())
                .Where(t => !existingTagNames.Contains(t.TagName))
                .ToList();

            try
            {
                context.DocumentTags.AddRange(uniqueNewTags);
                await context.SaveChangesAsync();
                
                var finalTags = await context.DocumentTags
                    .Where(t => t.JumpDocumentId == documentId)
                    .Select(t => new { t.TagName, t.TagCategory })
                    .ToListAsync();

                return Results.Ok(new {
                    success = true,
                    message = "Comprehensive tags saved successfully",
                    document = new { document.Id, document.Name },
                    existingTags,
                    newTagsGenerated = newTags.Count,
                    uniqueNewTags = uniqueNewTags.Count,
                    duplicatesRemoved = newTags.Count - uniqueNewTags.Count,
                    tagsAdded = uniqueNewTags.Count,
                    finalTagCount = finalTags.Count,
                    allNewTags = uniqueNewTags.Select(t => new { t.TagName, t.TagCategory }).ToList(),
                    finalTags
                });
            }
            catch (Exception saveEx)
            {
                return Results.Ok(new {
                    success = false,
                    message = "SaveChanges failed",
                    document = new { document.Id, document.Name },
                    existingTags,
                    newTagsGenerated = newTags.Count,
                    uniqueNewTags = uniqueNewTags.Count,
                    duplicatesRemoved = newTags.Count - uniqueNewTags.Count,
                    error = saveEx.Message,
                    innerError = saveEx.InnerException?.Message,
                    stackTrace = saveEx.StackTrace,
                    allNewTags = uniqueNewTags.Select(t => new { t.TagName, t.TagCategory }).ToList()
                });
            }
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { 
                success = false, 
                error = ex.Message,
                details = ex.InnerException?.Message,
                documentId = documentId
            });
        }
    }
    
    private static async Task<IResult> DebugComprehensiveTagging(JumpChainDbContext context, int documentId)
    {
        try
        {
            var document = await context.JumpDocuments
                .FirstOrDefaultAsync(d => d.Id == documentId);
                
            if (document == null)
            {
                return Results.NotFound(new { success = false, message = $"Document {documentId} not found" });
            }

            var diagnosticResults = new List<object>();

            diagnosticResults.Add(new {
                test = "Document Info",
                success = true,
                data = new {
                    id = document.Id,
                    name = document.Name,
                    folderPath = document.FolderPath,
                    sourceDrive = document.SourceDrive,
                    mimeType = document.MimeType,
                    size = document.Size
                }
            });

            try 
            {
                string contentType = DetermineContentType(document.Name ?? "", document.FolderPath ?? "");
                diagnosticResults.Add(new {
                    test = "ContentType Generation",
                    success = true,
                    data = new { contentType }
                });
            }
            catch (Exception ex)
            {
                diagnosticResults.Add(new {
                    test = "ContentType Generation",
                    success = false,
                    error = ex.Message
                });
            }

            try 
            {
                string fileFormat = DetermineFileFormat(document.MimeType ?? "", document.Name ?? "");
                diagnosticResults.Add(new {
                    test = "FileFormat Generation",
                    success = true,
                    data = new { fileFormat }
                });
            }
            catch (Exception ex)
            {
                diagnosticResults.Add(new {
                    test = "FileFormat Generation", 
                    success = false,
                    error = ex.Message
                });
            }

            try 
            {
                string sizeCategory = DetermineSizeCategory(document.Size);
                diagnosticResults.Add(new {
                    test = "Size Generation",
                    success = true,
                    data = new { sizeCategory }
                });
            }
            catch (Exception ex)
            {
                diagnosticResults.Add(new {
                    test = "Size Generation",
                    success = false,
                    error = ex.Message
                });
            }

            try 
            {
                var folders = (document.FolderPath ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries);
                var folderTags = folders.Length > 0 && !string.IsNullOrWhiteSpace(folders[0]) ? folders[0].Trim() : "No folder";
                diagnosticResults.Add(new {
                    test = "Folder Generation",
                    success = true,
                    data = new { folderTags, allFolders = folders }
                });
            }
            catch (Exception ex)
            {
                diagnosticResults.Add(new {
                    test = "Folder Generation",
                    success = false,
                    error = ex.Message
                });
            }

            try 
            {
                var qualityTags = new List<DocumentTag>();
                AddQualityTags(qualityTags, document.Name ?? "", document.FolderPath ?? "", document.Id);
                diagnosticResults.Add(new {
                    test = "Quality Tags Generation",
                    success = true,
                    data = new { 
                        qualityTagCount = qualityTags.Count,
                        qualityTags = qualityTags.Select(t => new { t.TagName, t.TagCategory }).ToList()
                    }
                });
            }
            catch (Exception ex)
            {
                diagnosticResults.Add(new {
                    test = "Quality Tags Generation",
                    success = false,
                    error = ex.Message
                });
            }

            try 
            {
                var seriesTags = new List<DocumentTag>();
                AddSeriesTags(seriesTags, document.Name ?? "", document.FolderPath ?? "", document.Id);
                diagnosticResults.Add(new {
                    test = "Series Tags Generation",
                    success = true,
                    data = new { 
                        seriesTagCount = seriesTags.Count,
                        seriesTags = seriesTags.Select(t => new { t.TagName, t.TagCategory }).ToList()
                    }
                });
            }
            catch (Exception ex)
            {
                diagnosticResults.Add(new {
                    test = "Series Tags Generation",
                    success = false,
                    error = ex.Message
                });
            }

            try 
            {
                var newTags = new List<DocumentTag>();

                newTags.Add(new DocumentTag { TagName = document.SourceDrive ?? "Unknown", TagCategory = "Drive", JumpDocumentId = document.Id });

                string contentType = DetermineContentType(document.Name ?? "", document.FolderPath ?? "");
                newTags.Add(new DocumentTag { TagName = contentType, TagCategory = "ContentType", JumpDocumentId = document.Id });

                string fileFormat = DetermineFileFormat(document.MimeType ?? "", document.Name ?? "");
                if (!string.IsNullOrEmpty(fileFormat))
                {
                    newTags.Add(new DocumentTag { TagName = fileFormat, TagCategory = "Format", JumpDocumentId = document.Id });
                }

                string sizeCategory = DetermineSizeCategory(document.Size);
                newTags.Add(new DocumentTag { TagName = sizeCategory, TagCategory = "Size", JumpDocumentId = document.Id });

                AddQualityTags(newTags, document.Name ?? "", document.FolderPath ?? "", document.Id);
                AddSeriesTags(newTags, document.Name ?? "", document.FolderPath ?? "", document.Id);
                TagGenerationHelpers.AddTextExtractionTag(newTags, document.ExtractedText, document.Id);

                diagnosticResults.Add(new {
                    test = "Full Comprehensive Tag Generation",
                    success = true,
                    data = new { 
                        totalTagCount = newTags.Count,
                        allTags = newTags.Select(t => new { t.TagName, t.TagCategory }).ToList()
                    }
                });
            }
            catch (Exception ex)
            {
                diagnosticResults.Add(new {
                    test = "Full Comprehensive Tag Generation",
                    success = false,
                    error = ex.Message,
                    details = ex.InnerException?.Message
                });
            }

            return Results.Ok(new {
                success = true,
                message = "Comprehensive tagging diagnostic completed",
                documentId,
                diagnosticResults
            });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { 
                success = false, 
                error = ex.Message,
                details = ex.InnerException?.Message,
                documentId = documentId
            });
        }
    }
    
    private static async Task<IResult> BatchSimpleTagging(IServiceProvider serviceProvider)
    {
        try
        {
            int batchSize = 1000;
            int totalProcessed = 0;
            int totalErrors = 0;
            var batchResults = new List<object>();

            int initialUntaggedCount;
            using (var scope = serviceProvider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<JumpChainDbContext>();
                initialUntaggedCount = await context.JumpDocuments
                    .CountAsync(d => !context.DocumentTags.Any(t => t.JumpDocumentId == d.Id));
            }

            if (initialUntaggedCount == 0)
            {
                return Results.Ok(new {
                    success = true,
                    message = "All documents already have tags!",
                    initialUntaggedCount = 0,
                    finalUntaggedCount = 0,
                    totalProcessed = 0,
                    completed = true
                });
            }

            int batchNumber = 0;
            while (true)
            {
                batchNumber++;
                int batchProcessed = 0;
                int batchErrors = 0;

                List<int> untaggedDocumentIds;
                using (var scope = serviceProvider.CreateScope())
                {
                    var context = scope.ServiceProvider.GetRequiredService<JumpChainDbContext>();
                    untaggedDocumentIds = await context.JumpDocuments
                        .Where(d => !context.DocumentTags.Any(t => t.JumpDocumentId == d.Id))
                        .Take(batchSize)
                        .Select(d => d.Id)
                        .ToListAsync();
                }

                if (!untaggedDocumentIds.Any())
                {
                    break;
                }

                foreach (var documentId in untaggedDocumentIds)
                {
                    try
                    {
                        using var scope = serviceProvider.CreateScope();
                        var context = scope.ServiceProvider.GetRequiredService<JumpChainDbContext>();
                        
                        var document = await context.JumpDocuments
                            .FirstOrDefaultAsync(d => d.Id == documentId);
                            
                        if (document == null)
                            continue;

                        var existingTagCount = await context.DocumentTags
                            .CountAsync(t => t.JumpDocumentId == documentId);
                            
                        if (existingTagCount > 0)
                            continue;

                        var newTags = new List<DocumentTag>
                        {
                            new DocumentTag { TagName = document.SourceDrive ?? "Unknown", TagCategory = "Drive", JumpDocumentId = document.Id },
                            new DocumentTag { TagName = "PDF", TagCategory = "Format", JumpDocumentId = document.Id }
                        };

                        context.DocumentTags.AddRange(newTags);
                        await context.SaveChangesAsync();
                        batchProcessed++;
                    }
                    catch (Exception docEx)
                    {
                        batchErrors++;
                        Console.WriteLine($"Error processing document {documentId}: {docEx.Message}");
                    }
                }

                totalProcessed += batchProcessed;
                totalErrors += batchErrors;

                batchResults.Add(new {
                    batchNumber,
                    documentsInBatch = untaggedDocumentIds.Count,
                    successfullyProcessed = batchProcessed,
                    errors = batchErrors,
                    totalProcessedSoFar = totalProcessed
                });

                Console.WriteLine($"Batch {batchNumber} completed: {batchProcessed}/{untaggedDocumentIds.Count} documents processed successfully. Total: {totalProcessed}");

                await Task.Delay(100);
            }

            int finalUntaggedCount;
            using (var scope = serviceProvider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<JumpChainDbContext>();
                finalUntaggedCount = await context.JumpDocuments
                    .CountAsync(d => !context.DocumentTags.Any(t => t.JumpDocumentId == d.Id));
            }

            return Results.Ok(new {
                success = true,
                message = $"Batch simple tagging completed! Processed {totalProcessed} documents in {batchNumber} batches.",
                initialUntaggedCount,
                finalUntaggedCount,
                totalProcessed,
                totalErrors,
                batchesCompleted = batchNumber,
                batchResults,
                completed = finalUntaggedCount == 0
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
    
    private static async Task<IResult> BatchComprehensiveTagging(IServiceProvider serviceProvider)
    {
        try
        {
            int batchSize = 500;
            int totalProcessed = 0;
            int totalUpgraded = 0;
            int totalErrors = 0;
            var batchResults = new List<object>();

            int initialCount;
            using (var scope = serviceProvider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<JumpChainDbContext>();
                initialCount = await context.JumpDocuments.CountAsync();
            }

            int batchNumber = 0;
            int skip = 0;
            
            while (true)
            {
                batchNumber++;
                int batchProcessed = 0;
                int batchUpgraded = 0;
                int batchErrors = 0;

                List<int> documentIds;
                using (var scope = serviceProvider.CreateScope())
                {
                    var context = scope.ServiceProvider.GetRequiredService<JumpChainDbContext>();
                    documentIds = await context.JumpDocuments
                        .Skip(skip)
                        .Take(batchSize)
                        .Select(d => d.Id)
                        .ToListAsync();
                }

                if (!documentIds.Any())
                {
                    break;
                }

                foreach (var documentId in documentIds)
                {
                    try
                    {
                        using var scope = serviceProvider.CreateScope();
                        var context = scope.ServiceProvider.GetRequiredService<JumpChainDbContext>();
                        
                        var document = await context.JumpDocuments
                            .FirstOrDefaultAsync(d => d.Id == documentId);
                            
                        if (document == null)
                            continue;

                        var existingTagsList = await context.DocumentTags
                            .Where(t => t.JumpDocumentId == documentId)
                            .Select(t => t.TagName)
                            .ToListAsync();
                        var existingTags = existingTagsList.ToHashSet();

                        var newTags = new List<DocumentTag>();

                        newTags.Add(new DocumentTag { TagName = document.SourceDrive ?? "Unknown", TagCategory = "Drive", JumpDocumentId = document.Id });

                        string contentType = DetermineContentType(document.Name ?? "", document.FolderPath ?? "");
                        newTags.Add(new DocumentTag { TagName = contentType, TagCategory = "ContentType", JumpDocumentId = document.Id });

                        string fileFormat = DetermineFileFormat(document.MimeType ?? "", document.Name ?? "");
                        if (!string.IsNullOrEmpty(fileFormat))
                        {
                            newTags.Add(new DocumentTag { TagName = fileFormat, TagCategory = "Format", JumpDocumentId = document.Id });
                        }

                        string sizeCategory = DetermineSizeCategory(document.Size);
                        newTags.Add(new DocumentTag { TagName = sizeCategory, TagCategory = "Size", JumpDocumentId = document.Id });

                        AddQualityTags(newTags, document.Name ?? "", document.FolderPath ?? "", document.Id);
                        AddSeriesTags(newTags, document.Name ?? "", document.FolderPath ?? "", document.Id);
                        TagGenerationHelpers.AddTextExtractionTag(newTags, document.ExtractedText, document.Id);

                        var uniqueNewTags = newTags
                            .GroupBy(t => new { t.TagName, t.TagCategory })
                            .Select(g => g.First())
                            .Where(t => !existingTags.Contains(t.TagName))
                            .ToList();

                        if (uniqueNewTags.Any())
                        {
                            context.DocumentTags.AddRange(uniqueNewTags);
                            await context.SaveChangesAsync();
                            batchUpgraded++;
                        }
                        
                        batchProcessed++;
                    }
                    catch (Exception docEx)
                    {
                        batchErrors++;
                        Console.WriteLine($"Error processing document {documentId}: {docEx.Message}");
                    }
                }

                totalProcessed += batchProcessed;
                totalUpgraded += batchUpgraded;
                totalErrors += batchErrors;
                skip += batchSize;

                batchResults.Add(new {
                    batchNumber,
                    documentsInBatch = documentIds.Count,
                    successfullyProcessed = batchProcessed,
                    documentsUpgraded = batchUpgraded,
                    errors = batchErrors,
                    totalProcessedSoFar = totalProcessed,
                    totalUpgradedSoFar = totalUpgraded
                });

                Console.WriteLine($"Batch {batchNumber} completed: {batchProcessed}/{documentIds.Count} documents processed, {batchUpgraded} upgraded. Total: {totalProcessed}");

                await Task.Delay(50);
            }

            int finalDocumentCount;
            int finalTagCount;
            using (var scope = serviceProvider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<JumpChainDbContext>();
                finalDocumentCount = await context.JumpDocuments.CountAsync();
                finalTagCount = await context.DocumentTags.CountAsync();
            }

            return Results.Ok(new {
                success = true,
                message = $"Comprehensive batch tagging completed! Processed {totalProcessed} documents, upgraded {totalUpgraded} with additional tags.",
                initialDocumentCount = initialCount,
                finalDocumentCount,
                finalTagCount,
                totalProcessed,
                totalUpgraded,
                totalErrors,
                batchesCompleted = batchNumber,
                batchResults,
                completed = true
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
    
    private static async Task<IResult> RemoveSfwTags(JumpChainDbContext context)
    {
        try
        {
            var sfwTags = await context.DocumentTags
                .Where(t => t.TagName == "SFW")
                .ToListAsync();

            if (!sfwTags.Any())
            {
                return Results.Ok(new {
                    success = true,
                    message = "No SFW tags found to remove",
                    removedCount = 0
                });
            }

            context.DocumentTags.RemoveRange(sfwTags);
            await context.SaveChangesAsync();

            return Results.Ok(new {
                success = true,
                message = $"Successfully removed {sfwTags.Count} SFW tags",
                removedCount = sfwTags.Count
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
    
    private static async Task<IResult> RemoveFolderTags(JumpChainDbContext context)
    {
        try
        {
            var folderTags = await context.DocumentTags
                .Where(t => t.TagCategory == "Folder")
                .ToListAsync();

            if (!folderTags.Any())
            {
                return Results.Ok(new {
                    success = true,
                    message = "No Folder tags found to remove",
                    removedCount = 0
                });
            }

            context.DocumentTags.RemoveRange(folderTags);
            await context.SaveChangesAsync();

            return Results.Ok(new {
                success = true,
                message = $"Successfully removed {folderTags.Count} Folder tags",
                removedCount = folderTags.Count
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
    
    // Helper methods for tag generation - delegate to TagGenerationHelpers
    private static string DetermineContentType(string fileName, string folderPath)
        => TagGenerationHelpers.DetermineContentType(fileName, folderPath);

    private static string DetermineFileFormat(string mimeType, string fileName)
        => TagGenerationHelpers.DetermineFileFormat(mimeType, fileName);

    private static string DetermineSizeCategory(long sizeBytes)
        => TagGenerationHelpers.DetermineSizeCategory(sizeBytes);

    private static void AddQualityTags(List<DocumentTag> tags, string fileName, string folderPath, int documentId)
        => TagGenerationHelpers.AddQualityTags(tags, fileName, folderPath, documentId);

    private static void AddSeriesTags(List<DocumentTag> tags, string fileName, string folderPath, int documentId)
        => TagGenerationHelpers.AddSeriesTags(tags, fileName, folderPath, documentId);

    private static async Task<IResult> RegenerateExtractionTags(JumpChainDbContext context)
    {
        try
        {
            // Remove all existing extraction-related tags (both old "HasText" and new "Has Text")
            var existingExtractionTags = await context.DocumentTags
                .Where(t => (t.TagName == "Has Text" || t.TagName == "HasText") && t.TagCategory == "Extraction")
                .ToListAsync();
            
            context.DocumentTags.RemoveRange(existingExtractionTags);
            await context.SaveChangesAsync();
            
            var removedCount = existingExtractionTags.Count;
            
            // Get all documents with extracted text
            var documentsWithText = await context.JumpDocuments
                .Where(d => d.ExtractedText != null && d.ExtractedText != "")
                .ToListAsync();
            
            // Create new "Has Text" tags for documents with extracted text
            var newTags = new List<DocumentTag>();
            foreach (var document in documentsWithText)
            {
                newTags.Add(new DocumentTag 
                { 
                    TagName = "Has Text", 
                    TagCategory = "Extraction", 
                    JumpDocumentId = document.Id 
                });
            }
            
            context.DocumentTags.AddRange(newTags);
            await context.SaveChangesAsync();
            
            return Results.Ok(new
            {
                success = true,
                message = "Successfully regenerated extraction tags",
                removedCount = removedCount,
                addedCount = newTags.Count,
                totalDocuments = await context.JumpDocuments.CountAsync(),
                documentsWithText = documentsWithText.Count
            });
        }
        catch (Exception ex)
        {
            return Results.Json(new
            {
                success = false,
                error = ex.Message,
                stackTrace = ex.StackTrace
            }, statusCode: 500);
        }
    }

    private static async Task<IResult> FixNarutoTags(JumpChainDbContext context)
    {
        try
        {
            // Get all documents with Naruto tag
            var narutoTaggedDocs = await context.DocumentTags
                .Where(t => t.TagName == "Naruto")
                .Include(t => t.JumpDocument)
                .ToListAsync();
            
            var totalNarutoTags = narutoTaggedDocs.Count;
            var removedTags = new List<DocumentTag>();
            
            // Check each document to see if it should actually have the Naruto tag
            foreach (var tag in narutoTaggedDocs)
            {
                if (tag.JumpDocument != null)
                {
                    string fullPath = $"{tag.JumpDocument.FolderPath}/{tag.JumpDocument.Name}".ToLowerInvariant();
                    
                    // Only keep the tag if it contains "naruto" or "konoha"
                    if (!fullPath.Contains("naruto") && !fullPath.Contains("konoha"))
                    {
                        removedTags.Add(tag);
                    }
                }
            }
            
            // Remove the incorrect tags
            context.DocumentTags.RemoveRange(removedTags);
            await context.SaveChangesAsync();
            
            return Results.Ok(new
            {
                success = true,
                message = "Successfully fixed Naruto tags",
                totalNarutoTags = totalNarutoTags,
                removedCount = removedTags.Count,
                remainingCount = totalNarutoTags - removedTags.Count
            });
        }
        catch (Exception ex)
        {
            return Results.Json(new
            {
                success = false,
                error = ex.Message,
                stackTrace = ex.StackTrace
            }, statusCode: 500);
        }
    }

    private static async Task<IResult> RegenerateFranchiseTags(JumpChainDbContext context)
    {
        try
        {
            // Remove all existing franchise/series tags
            var existingFranchiseTags = await context.DocumentTags
                .Where(t => t.TagCategory == "Series" || t.TagCategory == "Franchise")
                .ToListAsync();
            
            context.DocumentTags.RemoveRange(existingFranchiseTags);
            await context.SaveChangesAsync();
            
            var removedCount = existingFranchiseTags.Count;
            
            // Get all documents
            var allDocuments = await context.JumpDocuments.ToListAsync();
            
            // Regenerate franchise tags for each document
            var newTags = new List<DocumentTag>();
            foreach (var document in allDocuments)
            {
                var docTags = new List<DocumentTag>();
                TagGenerationHelpers.AddSeriesTags(docTags, document.Name ?? "", document.FolderPath ?? "", document.Id);
                newTags.AddRange(docTags);
            }
            
            context.DocumentTags.AddRange(newTags);
            await context.SaveChangesAsync();
            
            return Results.Ok(new
            {
                success = true,
                message = "Successfully regenerated franchise tags",
                removedCount = removedCount,
                addedCount = newTags.Count,
                totalDocuments = allDocuments.Count,
                documentsWithFranchiseTags = newTags.Select(t => t.JumpDocumentId).Distinct().Count()
            });
        }
        catch (Exception ex)
        {
            return Results.Json(new
            {
                success = false,
                error = ex.Message,
                stackTrace = ex.StackTrace
            }, statusCode: 500);
        }
    }

    private static async Task<IResult> RegenerateGenreTags(JumpChainDbContext context)
    {
        try
        {
            // Remove all existing genre tags
            var existingGenreTags = await context.DocumentTags
                .Where(t => t.TagCategory == "Genre")
                .ToListAsync();
            
            context.DocumentTags.RemoveRange(existingGenreTags);
            await context.SaveChangesAsync();
            
            var removedCount = existingGenreTags.Count;
            
            // Get all documents
            var allDocuments = await context.JumpDocuments.ToListAsync();
            
            // Regenerate genre tags for each document using GoogleDriveFileId matching
            var newTags = new List<DocumentTag>();
            var documentsWithFileIds = 0;
            var documentsWithoutFileIds = 0;
            
            foreach (var document in allDocuments)
            {
                if (!string.IsNullOrEmpty(document.GoogleDriveFileId))
                {
                    documentsWithFileIds++;
                    var docTags = new List<DocumentTag>();
                    TagGenerationHelpers.AddGenreTagsByFileId(docTags, document.GoogleDriveFileId, document.Id);
                    newTags.AddRange(docTags);
                }
                else
                {
                    documentsWithoutFileIds++;
                }
            }
            
            context.DocumentTags.AddRange(newTags);
            await context.SaveChangesAsync();
            
            return Results.Ok(new
            {
                success = true,
                message = "Successfully regenerated genre tags using file ID matching",
                removedCount = removedCount,
                addedCount = newTags.Count,
                totalDocuments = allDocuments.Count,
                documentsWithGenreTags = newTags.Select(t => t.JumpDocumentId).Distinct().Count(),
                documentsWithFileIds = documentsWithFileIds,
                documentsWithoutFileIds = documentsWithoutFileIds
            });
        }
        catch (Exception ex)
        {
            return Results.Json(new
            {
                success = false,
                error = ex.Message,
                stackTrace = ex.StackTrace
            }, statusCode: 500);
        }
    }
}

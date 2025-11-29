using JumpChainSearch.Data;
using JumpChainSearch.Models;
using JumpChainSearch.Services;
using Microsoft.EntityFrameworkCore;

namespace JumpChainSearch.Extensions;

public static class TagVotingEndpoints
{
    public static void MapTagVotingEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/voting");

        // Suggest adding a new tag
        group.MapPost("/suggest-tag", SuggestTag);
        
        // Request removing an existing tag
        group.MapPost("/request-removal", RequestTagRemoval);
        
        // Vote on a suggestion or removal
        group.MapPost("/vote", CastVote);
        
        // Get pending suggestions/removals for a document
        group.MapGet("/document/{documentId}/pending", GetPendingForDocument);
        
        // Get all pending suggestions/removals (for admin)
        group.MapGet("/pending", GetAllPending);
        
        // Get voting configuration
        group.MapGet("/config", GetVotingConfig);
        
        // Update voting configuration (admin only)
        group.MapPost("/config", UpdateVotingConfig);
        
        // Manually approve/reject suggestion (admin only)
        group.MapPost("/admin/approve-suggestion/{id}", ApproveTagSuggestion);
        group.MapPost("/admin/reject-suggestion/{id}", RejectTagSuggestion);
        group.MapPost("/admin/approve-removal/{id}", ApproveTagRemoval);
        group.MapPost("/admin/reject-removal/{id}", RejectTagRemoval);
        
        // Check if threshold met and auto-apply
        group.MapPost("/check-thresholds", CheckAndApplyThresholds);
        
        // Get user's tag overrides
        group.MapGet("/user/{userId}/overrides", GetUserOverrides);
        
        // Track document view
        group.MapPost("/track-view", TrackDocumentView);
        
        // Approved Tag Rules Management
        group.MapGet("/rules", GetApprovedRules);
        group.MapPost("/rules/apply", ApplyApprovedRules);
        group.MapPost("/rules/{id}/toggle", ToggleRule);
        group.MapDelete("/rules/{id}", DeleteRule);
        group.MapPost("/rules/create", CreateManualRule);
    }

    private static async Task<IResult> SuggestTag(
        JumpChainDbContext context,
        SuggestTagRequest request)
    {
        try
        {
            // Check if document exists
            var document = await context.JumpDocuments.FindAsync(request.DocumentId);
            if (document == null)
                return Results.NotFound(new { success = false, message = "Document not found" });

            // Check if tag already exists on document
            var existingTag = await context.DocumentTags
                .FirstOrDefaultAsync(t => t.JumpDocumentId == request.DocumentId && 
                                        t.TagName == request.TagName && 
                                        t.TagCategory == request.TagCategory);
            
            if (existingTag != null)
                return Results.BadRequest(new { success = false, message = "Tag already exists on this document" });

            // Check if there's already a pending suggestion
            var existingSuggestion = await context.TagSuggestions
                .FirstOrDefaultAsync(s => s.JumpDocumentId == request.DocumentId && 
                                        s.TagName == request.TagName && 
                                        s.TagCategory == request.TagCategory &&
                                        s.Status == "Pending");
            
            if (existingSuggestion != null)
                return Results.BadRequest(new { success = false, message = "This tag is already pending approval", suggestionId = existingSuggestion.Id });

            // Create suggestion
            var suggestion = new TagSuggestion
            {
                JumpDocumentId = request.DocumentId,
                TagName = request.TagName,
                TagCategory = request.TagCategory,
                SuggestedByUserId = request.UserId,
                CreatedAt = DateTime.UtcNow,
                Status = "Pending"
            };

            context.TagSuggestions.Add(suggestion);
            await context.SaveChangesAsync();

            // Create user override (instant UX)
            var userOverride = new UserTagOverride
            {
                UserId = request.UserId,
                JumpDocumentId = request.DocumentId,
                TagName = request.TagName,
                TagCategory = request.TagCategory,
                IsAdded = true,
                CreatedAt = DateTime.UtcNow
            };

            context.UserTagOverrides.Add(userOverride);
            
            // Auto-create vote in favor from the suggester
            var autoVote = new TagVote
            {
                TagSuggestionId = suggestion.Id,
                TagRemovalRequestId = null,
                UserId = request.UserId,
                IsInFavor = true,
                Weight = 1.0,
                CreatedAt = DateTime.UtcNow
            };
            
            context.TagVotes.Add(autoVote);
            await context.SaveChangesAsync();

            return Results.Ok(new { 
                success = true, 
                message = "Tag suggestion created", 
                suggestionId = suggestion.Id,
                userOverrideCreated = true,
                autoVoteCreated = true
            });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Error creating tag suggestion: {ex.Message}");
        }
    }

    private static async Task<IResult> RequestTagRemoval(
        JumpChainDbContext context,
        RequestTagRemovalRequest request)
    {
        try
        {
            // Find the tag
            var documentTag = await context.DocumentTags
                .FirstOrDefaultAsync(t => t.JumpDocumentId == request.DocumentId && 
                                        t.TagName == request.TagName && 
                                        t.TagCategory == request.TagCategory);
            
            if (documentTag == null)
                return Results.NotFound(new { success = false, message = "Tag not found on this document" });

            // Check if there's already a pending removal request
            var existingRequest = await context.TagRemovalRequests
                .FirstOrDefaultAsync(r => r.JumpDocumentId == request.DocumentId && 
                                        r.TagName == request.TagName && 
                                        r.TagCategory == request.TagCategory &&
                                        r.Status == "Pending");
            
            if (existingRequest != null)
                return Results.BadRequest(new { success = false, message = "Removal request already pending", requestId = existingRequest.Id });

            // Create removal request
            var removalRequest = new TagRemovalRequest
            {
                JumpDocumentId = request.DocumentId,
                DocumentTagId = documentTag.Id,
                TagName = request.TagName,
                TagCategory = request.TagCategory,
                RequestedByUserId = request.UserId,
                CreatedAt = DateTime.UtcNow,
                Status = "Pending"
            };

            context.TagRemovalRequests.Add(removalRequest);
            await context.SaveChangesAsync();

            // Create user override (instant UX - user won't see this tag)
            var userOverride = new UserTagOverride
            {
                UserId = request.UserId,
                JumpDocumentId = request.DocumentId,
                TagName = request.TagName,
                TagCategory = request.TagCategory,
                IsAdded = false, // False means user removed it
                CreatedAt = DateTime.UtcNow
            };

            context.UserTagOverrides.Add(userOverride);
            
            // Auto-create vote in favor from the requester
            var autoVote = new TagVote
            {
                TagSuggestionId = null,
                TagRemovalRequestId = removalRequest.Id,
                UserId = request.UserId,
                IsInFavor = true,
                Weight = 1.0,
                CreatedAt = DateTime.UtcNow
            };
            
            context.TagVotes.Add(autoVote);
            await context.SaveChangesAsync();

            return Results.Ok(new { 
                success = true, 
                message = "Tag removal request created", 
                requestId = removalRequest.Id,
                userOverrideCreated = true,
                autoVoteCreated = true
            });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Error creating removal request: {ex.Message}");
        }
    }

    private static async Task<IResult> CastVote(
        JumpChainDbContext context,
        CastVoteRequest request)
    {
        try
        {
            if (request.SuggestionId == null && request.RemovalRequestId == null)
                return Results.BadRequest(new { success = false, message = "Must specify either suggestionId or removalRequestId" });

            // Check if user already voted
            var existingVote = await context.TagVotes
                .FirstOrDefaultAsync(v => v.UserId == request.UserId && 
                                        v.TagSuggestionId == request.SuggestionId && 
                                        v.TagRemovalRequestId == request.RemovalRequestId);
            
            if (existingVote != null)
            {
                // Update existing vote
                existingVote.IsInFavor = request.IsInFavor;
                existingVote.Weight = 1.0; // Reset weight
                existingVote.CreatedAt = DateTime.UtcNow;
            }
            else
            {
                // Create new vote
                var vote = new TagVote
                {
                    UserId = request.UserId,
                    TagSuggestionId = request.SuggestionId,
                    TagRemovalRequestId = request.RemovalRequestId,
                    IsInFavor = request.IsInFavor,
                    CreatedAt = DateTime.UtcNow,
                    Weight = 1.0
                };

                context.TagVotes.Add(vote);
            }

            await context.SaveChangesAsync();

            // Get vote counts
            var votes = await context.TagVotes
                .Where(v => v.TagSuggestionId == request.SuggestionId && v.TagRemovalRequestId == request.RemovalRequestId)
                .ToListAsync();

            var favorVotes = votes.Where(v => v.IsInFavor).Sum(v => v.Weight);
            var againstVotes = votes.Where(v => !v.IsInFavor).Sum(v => v.Weight);
            var totalVotes = votes.Sum(v => v.Weight);

            return Results.Ok(new { 
                success = true, 
                message = "Vote recorded",
                voteStats = new {
                    favorVotes,
                    againstVotes,
                    totalVotes,
                    agreementPercentage = totalVotes > 0 ? (favorVotes / totalVotes * 100) : 0
                }
            });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Error casting vote: {ex.Message}");
        }
    }

    private static async Task<IResult> GetPendingForDocument(JumpChainDbContext context, int documentId)
    {
        try
        {
            var suggestions = await context.TagSuggestions
                .Where(s => s.JumpDocumentId == documentId && s.Status == "Pending")
                .Include(s => s.JumpDocument)
                .Include(s => s.Votes)
                .Select(s => new {
                    s.Id,
                    s.JumpDocumentId,
                    DocumentName = s.JumpDocument.Name,
                    s.TagName,
                    s.TagCategory,
                    s.SuggestedByUserId,
                    s.CreatedAt,
                    VoteCount = s.Votes.Count,
                    FavorVotes = s.Votes.Where(v => v.IsInFavor).Sum(v => v.Weight),
                    AgainstVotes = s.Votes.Where(v => !v.IsInFavor).Sum(v => v.Weight),
                    TotalVotes = s.Votes.Sum(v => v.Weight),
                    AgreementPercentage = s.Votes.Sum(v => v.Weight) > 0 ? 
                        s.Votes.Where(v => v.IsInFavor).Sum(v => v.Weight) / s.Votes.Sum(v => v.Weight) * 100 : 0
                })
                .ToListAsync();

            var removalRequests = await context.TagRemovalRequests
                .Where(r => r.JumpDocumentId == documentId && r.Status == "Pending")
                .Include(r => r.JumpDocument)
                .Include(r => r.Votes)
                .Select(r => new {
                    r.Id,
                    r.JumpDocumentId,
                    DocumentName = r.JumpDocument.Name,
                    GoogleDriveLink = r.JumpDocument.WebViewLink,
                    r.TagName,
                    r.TagCategory,
                    r.RequestedByUserId,
                    r.CreatedAt,
                    VoteCount = r.Votes.Count,
                    FavorVotes = r.Votes.Where(v => v.IsInFavor).Sum(v => v.Weight),
                    AgainstVotes = r.Votes.Where(v => !v.IsInFavor).Sum(v => v.Weight),
                    TotalVotes = r.Votes.Sum(v => v.Weight),
                    AgreementPercentage = r.Votes.Sum(v => v.Weight) > 0 ? 
                        r.Votes.Where(v => v.IsInFavor).Sum(v => v.Weight) / r.Votes.Sum(v => v.Weight) * 100 : 0
                })
                .ToListAsync();

            return Results.Ok(new { 
                success = true, 
                pendingSuggestions = suggestions, 
                pendingRemovalRequests = removalRequests 
            });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Error getting pending items: {ex.Message}");
        }
    }

    private static async Task<IResult> GetAllPending(HttpContext context, JumpChainDbContext dbContext, AdminAuthService authService)
    {
        var (valid, user) = await ValidateSession(context, authService);
        if (!valid)
            return Results.Unauthorized();

        try
        {
            var suggestions = await dbContext.TagSuggestions
                .Where(s => s.Status == "Pending")
                .Include(s => s.JumpDocument)
                .Include(s => s.Votes)
                .ToListAsync();

            var suggestionDtos = suggestions.Select(s => new {
                s.Id,
                s.JumpDocumentId,
                DocumentName = s.JumpDocument.Name,
                GoogleDriveLink = s.JumpDocument.WebViewLink,
                s.TagName,
                s.TagCategory,
                s.SuggestedByUserId,
                s.CreatedAt,
                VoteCount = s.Votes.Count,
                FavorVotes = s.Votes.Where(v => v.IsInFavor).Sum(v => v.Weight),
                AgainstVotes = s.Votes.Where(v => !v.IsInFavor).Sum(v => v.Weight),
                AgreementPercentage = s.Votes.Sum(v => v.Weight) > 0 ? 
                    s.Votes.Where(v => v.IsInFavor).Sum(v => v.Weight) / s.Votes.Sum(v => v.Weight) * 100 : 0
            })
            .OrderByDescending(s => s.VoteCount)
            .ToList();

            var removalRequests = await dbContext.TagRemovalRequests
                .Where(r => r.Status == "Pending")
                .Include(r => r.JumpDocument)
                .Include(r => r.Votes)
                .ToListAsync();

            var removalDtos = removalRequests.Select(r => new {
                r.Id,
                r.JumpDocumentId,
                DocumentName = r.JumpDocument.Name,
                GoogleDriveLink = r.JumpDocument.WebViewLink,
                r.TagName,
                r.TagCategory,
                r.RequestedByUserId,
                r.CreatedAt,
                VoteCount = r.Votes.Count,
                FavorVotes = r.Votes.Where(v => v.IsInFavor).Sum(v => v.Weight),
                AgainstVotes = r.Votes.Where(v => !v.IsInFavor).Sum(v => v.Weight),
                AgreementPercentage = r.Votes.Sum(v => v.Weight) > 0 ? 
                    r.Votes.Where(v => v.IsInFavor).Sum(v => v.Weight) / r.Votes.Sum(v => v.Weight) * 100 : 0
            })
            .OrderByDescending(r => r.VoteCount)
            .ToList();

            return Results.Ok(new { 
                success = true, 
                pendingSuggestions = suggestionDtos, 
                pendingRemovalRequests = removalDtos,
                totalPending = suggestionDtos.Count + removalDtos.Count
            });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Error getting all pending items: {ex.Message}");
        }
    }

    private static async Task<IResult> GetVotingConfig(JumpChainDbContext context)
    {
        try
        {
            var config = await context.VotingConfigurations.FirstOrDefaultAsync();
            
            if (config == null)
            {
                // Create default config
                config = new VotingConfiguration
                {
                    MinimumVotesRequired = 50,
                    RequiredAgreementPercentage = 70.0,
                    ScaleByPopularity = true,
                    PopularityScaleFactor = 0.05,
                    MaximumVotesRequired = 200,
                    VoteDecayStartDays = 90,
                    VoteDecayRatePerDay = 0.01,
                    AutoApplyEnabled = true,
                    LastModified = DateTime.UtcNow,
                    ModifiedBy = "System"
                };

                context.VotingConfigurations.Add(config);
                await context.SaveChangesAsync();
            }

            return Results.Ok(new { success = true, config });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Error getting voting config: {ex.Message}");
        }
    }

    private static async Task<IResult> UpdateVotingConfig(
        HttpContext httpContext,
        JumpChainDbContext context,
        AdminAuthService authService,
        UpdateVotingConfigRequest request)
    {
        var (valid, user) = await ValidateSession(httpContext, authService);
        if (!valid)
            return Results.Unauthorized();

        try
        {
            var config = await context.VotingConfigurations.FirstOrDefaultAsync();
            
            if (config == null)
            {
                config = new VotingConfiguration();
                context.VotingConfigurations.Add(config);
            }

            config.MinimumVotesRequired = request.MinimumVotesRequired;
            config.RequiredAgreementPercentage = request.RequiredAgreementPercentage;
            config.ScaleByPopularity = request.ScaleByPopularity;
            config.PopularityScaleFactor = request.PopularityScaleFactor;
            config.MaximumVotesRequired = request.MaximumVotesRequired;
            config.VoteDecayStartDays = request.VoteDecayStartDays;
            config.VoteDecayRatePerDay = request.VoteDecayRatePerDay;
            config.AutoApplyEnabled = request.AutoApplyEnabled;
            config.LastModified = DateTime.UtcNow;
            config.ModifiedBy = "Admin";

            await context.SaveChangesAsync();

            return Results.Ok(new { success = true, message = "Voting configuration updated", config });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Error updating voting config: {ex.Message}");
        }
    }

    private static async Task<IResult> ApproveTagSuggestion(HttpContext httpContext, JumpChainDbContext context, AdminAuthService authService, int id, string? categoryOverride = null)
    {
        var (valid, user) = await ValidateSession(httpContext, authService);
        if (!valid)
            return Results.Unauthorized();

        try
        {
            var suggestion = await context.TagSuggestions
                .Include(s => s.JumpDocument)
                .Include(s => s.Votes)
                .FirstOrDefaultAsync(s => s.Id == id);
                
            if (suggestion == null)
                return Results.NotFound(new { success = false, message = "Suggestion not found" });

            // Use override category if provided, otherwise use suggestion's category
            var finalCategory = !string.IsNullOrWhiteSpace(categoryOverride) ? categoryOverride : suggestion.TagCategory;

            // Check if tag already exists
            var existingTag = await context.DocumentTags
                .FirstOrDefaultAsync(t => t.JumpDocumentId == suggestion.JumpDocumentId && 
                                        t.TagName == suggestion.TagName && 
                                        t.TagCategory == finalCategory);

            if (existingTag == null)
            {
                // Add the tag with the final category
                var newTag = new DocumentTag
                {
                    JumpDocumentId = suggestion.JumpDocumentId,
                    TagName = suggestion.TagName,
                    TagCategory = finalCategory
                };

                context.DocumentTags.Add(newTag);
            }

            suggestion.Status = "Applied";
            suggestion.AppliedAt = DateTime.UtcNow;

            // Create ApprovedTagRule for persistence across regenerations
            var rule = new ApprovedTagRule
            {
                GoogleDriveFileId = suggestion.JumpDocument.GoogleDriveFileId,
                DocumentName = suggestion.JumpDocument.Name,
                TagName = suggestion.TagName,
                TagCategory = finalCategory,
                RuleType = "Add",
                ApprovalSource = "AdminApproval",
                ApprovedByUserId = user?.Username ?? "admin",
                TagSuggestionId = suggestion.Id,
                VotesInFavor = suggestion.Votes.Where(v => v.IsInFavor).Sum(v => (int)v.Weight),
                TotalVotes = suggestion.Votes.Sum(v => (int)v.Weight),
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            context.ApprovedTagRules.Add(rule);

            // Remove all user overrides for this tag (it's now official)
            var overrides = await context.UserTagOverrides
                .Where(o => o.JumpDocumentId == suggestion.JumpDocumentId && 
                          o.TagName == suggestion.TagName && 
                          o.TagCategory == finalCategory &&
                          o.IsAdded == true)
                .ToListAsync();

            context.UserTagOverrides.RemoveRange(overrides);

            await context.SaveChangesAsync();

            return Results.Ok(new { 
                success = true, 
                message = "Tag suggestion approved and applied", 
                overridesRemoved = overrides.Count,
                ruleCreated = true,
                ruleId = rule.Id
            });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Error approving suggestion: {ex.Message}");
        }
    }

    private static async Task<IResult> RejectTagSuggestion(HttpContext httpContext, JumpChainDbContext context, AdminAuthService authService, int id)
    {
        var (valid, user) = await ValidateSession(httpContext, authService);
        if (!valid)
            return Results.Unauthorized();

        try
        {
            var suggestion = await context.TagSuggestions.FindAsync(id);
            if (suggestion == null)
                return Results.NotFound(new { success = false, message = "Suggestion not found" });

            suggestion.Status = "Rejected";
            suggestion.RejectionReason = null;

            // Remove user overrides for this tag
            var overrides = await context.UserTagOverrides
                .Where(o => o.JumpDocumentId == suggestion.JumpDocumentId && 
                          o.TagName == suggestion.TagName && 
                          o.TagCategory == suggestion.TagCategory &&
                          o.IsAdded == true)
                .ToListAsync();

            context.UserTagOverrides.RemoveRange(overrides);

            await context.SaveChangesAsync();

            return Results.Ok(new { success = true, message = "Tag suggestion rejected", overridesRemoved = overrides.Count });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Error rejecting suggestion: {ex.Message}");
        }
    }

    private static async Task<IResult> ApproveTagRemoval(HttpContext httpContext, JumpChainDbContext context, AdminAuthService authService, int id)
    {
        var (valid, user) = await ValidateSession(httpContext, authService);
        if (!valid)
            return Results.Unauthorized();

        try
        {
            var request = await context.TagRemovalRequests
                .Include(r => r.JumpDocument)
                .Include(r => r.Votes)
                .FirstOrDefaultAsync(r => r.Id == id);
                
            if (request == null)
                return Results.NotFound(new { success = false, message = "Removal request not found" });

            // Remove the tag if it exists
            if (request.DocumentTagId.HasValue)
            {
                var tag = await context.DocumentTags.FindAsync(request.DocumentTagId.Value);
                if (tag != null)
                {
                    context.DocumentTags.Remove(tag);
                }
            }
            
            // Update request status and clear the foreign key reference
            request.Status = "Removed";
            request.RemovedAt = DateTime.UtcNow;
            request.DocumentTagId = null; // Clear the foreign key

            // Create ApprovedTagRule for persistence across regenerations
            var rule = new ApprovedTagRule
            {
                GoogleDriveFileId = request.JumpDocument.GoogleDriveFileId,
                DocumentName = request.JumpDocument.Name,
                TagName = request.TagName,
                TagCategory = request.TagCategory,
                RuleType = "Remove",
                ApprovalSource = "AdminApproval",
                ApprovedByUserId = user?.Username ?? "admin",
                TagRemovalRequestId = request.Id,
                VotesInFavor = request.Votes.Where(v => v.IsInFavor).Sum(v => (int)v.Weight),
                TotalVotes = request.Votes.Sum(v => (int)v.Weight),
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            context.ApprovedTagRules.Add(rule);

            // Remove all user overrides for this tag
            var overrides = await context.UserTagOverrides
                .Where(o => o.JumpDocumentId == request.JumpDocumentId && 
                          o.TagName == request.TagName && 
                          o.TagCategory == request.TagCategory)
                .ToListAsync();

            context.UserTagOverrides.RemoveRange(overrides);

            await context.SaveChangesAsync();

            return Results.Ok(new { 
                success = true, 
                message = "Tag removal approved and executed", 
                overridesRemoved = overrides.Count,
                ruleCreated = true,
                ruleId = rule.Id
            });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Error approving removal: {ex.Message}");
        }
    }

    private static async Task<IResult> RejectTagRemoval(HttpContext httpContext, JumpChainDbContext context, AdminAuthService authService, int id)
    {
        var (valid, user) = await ValidateSession(httpContext, authService);
        if (!valid)
            return Results.Unauthorized();

        try
        {
            var removalRequest = await context.TagRemovalRequests.FindAsync(id);
            if (removalRequest == null)
                return Results.NotFound(new { success = false, message = "Removal request not found" });

            removalRequest.Status = "Rejected";
            removalRequest.RejectionReason = null;

            // Remove user overrides for this tag (tag stays on document)
            var overrides = await context.UserTagOverrides
                .Where(o => o.JumpDocumentId == removalRequest.JumpDocumentId && 
                          o.TagName == removalRequest.TagName && 
                          o.TagCategory == removalRequest.TagCategory &&
                          o.IsAdded == false)
                .ToListAsync();

            context.UserTagOverrides.RemoveRange(overrides);

            await context.SaveChangesAsync();

            return Results.Ok(new { success = true, message = "Tag removal rejected", overridesRemoved = overrides.Count });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Error rejecting removal: {ex.Message}");
        }
    }

    private static async Task<IResult> CheckAndApplyThresholds(HttpContext httpContext, JumpChainDbContext context, AdminAuthService authService)
    {
        var (valid, user) = await ValidateSession(httpContext, authService);
        if (!valid)
            return Results.Unauthorized();

        try
        {
            var config = await context.VotingConfigurations.FirstOrDefaultAsync();
            if (config == null || !config.AutoApplyEnabled)
                return Results.Ok(new { success = true, message = "Auto-apply is disabled", appliedCount = 0 });

            int appliedSuggestions = 0;
            int appliedRemovals = 0;

            // Check suggestions
            var pendingSuggestions = await context.TagSuggestions
                .Where(s => s.Status == "Pending")
                .Include(s => s.Votes)
                .Include(s => s.JumpDocument)
                .ToListAsync();

            foreach (var suggestion in pendingSuggestions)
            {
                var totalVotes = suggestion.Votes.Sum(v => v.Weight);
                var favorVotes = suggestion.Votes.Where(v => v.IsInFavor).Sum(v => v.Weight);
                
                // Get document view count for popularity scaling
                int requiredVotes = config.MinimumVotesRequired;
                if (config.ScaleByPopularity)
                {
                    var viewCount = await context.DocumentViewCounts
                        .Where(vc => vc.JumpDocumentId == suggestion.JumpDocumentId)
                        .Select(vc => vc.ViewCount)
                        .FirstOrDefaultAsync();

                    if (viewCount > 0)
                    {
                        requiredVotes = Math.Min(
                            (int)(viewCount * config.PopularityScaleFactor),
                            config.MaximumVotesRequired
                        );
                        requiredVotes = Math.Max(requiredVotes, config.MinimumVotesRequired);
                    }
                }

                if (totalVotes >= requiredVotes)
                {
                    var agreementPercentage = (favorVotes / totalVotes) * 100;
                    
                    if (agreementPercentage >= config.RequiredAgreementPercentage)
                    {
                        // Apply the suggestion
                        var existingTag = await context.DocumentTags
                            .FirstOrDefaultAsync(t => t.JumpDocumentId == suggestion.JumpDocumentId && 
                                                    t.TagName == suggestion.TagName && 
                                                    t.TagCategory == suggestion.TagCategory);

                        if (existingTag == null)
                        {
                            context.DocumentTags.Add(new DocumentTag
                            {
                                JumpDocumentId = suggestion.JumpDocumentId,
                                TagName = suggestion.TagName,
                                TagCategory = suggestion.TagCategory
                            });
                        }

                        suggestion.Status = "Applied";
                        suggestion.AppliedAt = DateTime.UtcNow;
                        
                        // Create ApprovedTagRule for persistence across regenerations
                        var rule = new ApprovedTagRule
                        {
                            GoogleDriveFileId = suggestion.JumpDocument.GoogleDriveFileId,
                            DocumentName = suggestion.JumpDocument.Name,
                            TagName = suggestion.TagName,
                            TagCategory = suggestion.TagCategory,
                            RuleType = "Add",
                            ApprovalSource = "CommunityVote",
                            ApprovedByUserId = suggestion.SuggestedByUserId,
                            TagSuggestionId = suggestion.Id,
                            VotesInFavor = (int)favorVotes,
                            TotalVotes = (int)totalVotes,
                            IsActive = true,
                            CreatedAt = DateTime.UtcNow
                        };

                        context.ApprovedTagRules.Add(rule);
                        appliedSuggestions++;
                    }
                }
            }

            // Check removal requests
            var pendingRemovals = await context.TagRemovalRequests
                .Where(r => r.Status == "Pending")
                .Include(r => r.Votes)
                .Include(r => r.DocumentTag)
                .Include(r => r.JumpDocument)
                .ToListAsync();

            foreach (var request in pendingRemovals)
            {
                var totalVotes = request.Votes.Sum(v => v.Weight);
                var favorVotes = request.Votes.Where(v => v.IsInFavor).Sum(v => v.Weight);
                
                int requiredVotes = config.MinimumVotesRequired;
                if (config.ScaleByPopularity)
                {
                    var viewCount = await context.DocumentViewCounts
                        .Where(vc => vc.JumpDocumentId == request.JumpDocumentId)
                        .Select(vc => vc.ViewCount)
                        .FirstOrDefaultAsync();

                    if (viewCount > 0)
                    {
                        requiredVotes = Math.Min(
                            (int)(viewCount * config.PopularityScaleFactor),
                            config.MaximumVotesRequired
                        );
                        requiredVotes = Math.Max(requiredVotes, config.MinimumVotesRequired);
                    }
                }

                if (totalVotes >= requiredVotes)
                {
                    var agreementPercentage = (favorVotes / totalVotes) * 100;
                    
                    if (agreementPercentage >= config.RequiredAgreementPercentage)
                    {
                        // Remove the tag
                        if (request.DocumentTag != null)
                        {
                            context.DocumentTags.Remove(request.DocumentTag);
                        }

                        request.Status = "Removed";
                        request.RemovedAt = DateTime.UtcNow;
                        
                        // Create ApprovedTagRule for persistence across regenerations
                        var rule = new ApprovedTagRule
                        {
                            GoogleDriveFileId = request.JumpDocument.GoogleDriveFileId,
                            DocumentName = request.JumpDocument.Name,
                            TagName = request.TagName,
                            TagCategory = request.TagCategory,
                            RuleType = "Remove",
                            ApprovalSource = "CommunityVote",
                            ApprovedByUserId = request.RequestedByUserId,
                            TagRemovalRequestId = request.Id,
                            VotesInFavor = (int)favorVotes,
                            TotalVotes = (int)totalVotes,
                            IsActive = true,
                            CreatedAt = DateTime.UtcNow
                        };

                        context.ApprovedTagRules.Add(rule);
                        appliedRemovals++;
                    }
                }
            }

            await context.SaveChangesAsync();

            return Results.Ok(new { 
                success = true, 
                message = "Threshold check complete", 
                appliedSuggestions,
                appliedRemovals,
                totalApplied = appliedSuggestions + appliedRemovals
            });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Error checking thresholds: {ex.Message}");
        }
    }

    private static async Task<IResult> GetUserOverrides(JumpChainDbContext context, string userId)
    {
        try
        {
            var overrides = await context.UserTagOverrides
                .Where(o => o.UserId == userId)
                .Select(o => new {
                    o.JumpDocumentId,
                    o.TagName,
                    o.TagCategory,
                    o.IsAdded
                })
                .ToListAsync();

            return Results.Ok(new { success = true, overrides });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Error getting user overrides: {ex.Message}");
        }
    }

    private static async Task<IResult> TrackDocumentView(JumpChainDbContext context, TrackViewRequest request)
    {
        try
        {
            var viewCount = await context.DocumentViewCounts
                .FirstOrDefaultAsync(vc => vc.JumpDocumentId == request.DocumentId);

            if (viewCount == null)
            {
                viewCount = new DocumentViewCount
                {
                    JumpDocumentId = request.DocumentId,
                    ViewCount = 1,
                    UniqueViewCount = 1,
                    LastViewed = DateTime.UtcNow
                };
                context.DocumentViewCounts.Add(viewCount);
            }
            else
            {
                viewCount.ViewCount++;
                viewCount.LastViewed = DateTime.UtcNow;
                // Note: Tracking unique users would require separate table to track user-document views
            }

            await context.SaveChangesAsync();

            return Results.Ok(new { success = true, viewCount = viewCount.ViewCount });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Error tracking view: {ex.Message}");
        }
    }

    private static async Task<IResult> GetApprovedRules(HttpContext httpContext, JumpChainDbContext context, AdminAuthService authService, bool? activeOnly = true)
    {
        var (valid, user) = await ValidateSession(httpContext, authService);
        if (!valid)
            return Results.Unauthorized();

        try
        {
            var query = context.ApprovedTagRules.AsQueryable();
            
            if (activeOnly == true)
            {
                query = query.Where(r => r.IsActive);
            }

            var rules = await query
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => new {
                    r.Id,
                    r.GoogleDriveFileId,
                    r.DocumentName,
                    r.TagName,
                    r.TagCategory,
                    r.RuleType,
                    r.ApprovalSource,
                    r.ApprovedByUserId,
                    r.CreatedAt,
                    r.VotesInFavor,
                    r.TotalVotes,
                    r.IsActive,
                    r.LastAppliedAt,
                    r.TimesApplied,
                    r.Notes
                })
                .ToListAsync();

            return Results.Ok(new { 
                success = true, 
                rules,
                totalCount = rules.Count,
                activeCount = rules.Count(r => r.IsActive)
            });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Error getting approved rules: {ex.Message}");
        }
    }

    private static async Task<IResult> ApplyApprovedRules(HttpContext httpContext, TagRuleService tagRuleService, AdminAuthService authService, ApplyRulesRequest? request)
    {
        var (valid, user) = await ValidateSession(httpContext, authService);
        if (!valid)
            return Results.Unauthorized();

        try
        {
            var result = await tagRuleService.ApplyApprovedRules();

            return Results.Ok(new { 
                success = true, 
                message = "Approved rules applied successfully",
                totalRules = result.TotalRules,
                additionsApplied = result.AdditionsApplied,
                removalsApplied = result.RemovalsApplied,
                documentsNotFound = result.DocumentsNotFound,
                appliedRuleIds = result.AppliedRuleIds
            });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Error applying approved rules: {ex.Message}");
        }
    }

    private static async Task<IResult> ToggleRule(HttpContext httpContext, JumpChainDbContext context, AdminAuthService authService, int id)
    {
        var (valid, user) = await ValidateSession(httpContext, authService);
        if (!valid)
            return Results.Unauthorized();

        try
        {
            var rule = await context.ApprovedTagRules.FindAsync(id);
            if (rule == null)
                return Results.NotFound(new { success = false, message = "Rule not found" });

            rule.IsActive = !rule.IsActive;
            await context.SaveChangesAsync();

            return Results.Ok(new { 
                success = true, 
                message = $"Rule {(rule.IsActive ? "activated" : "deactivated")}",
                ruleId = rule.Id,
                isActive = rule.IsActive
            });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Error toggling rule: {ex.Message}");
        }
    }

    private static async Task<IResult> DeleteRule(HttpContext httpContext, JumpChainDbContext context, AdminAuthService authService, int id)
    {
        var (valid, user) = await ValidateSession(httpContext, authService);
        if (!valid)
            return Results.Unauthorized();

        try
        {
            var rule = await context.ApprovedTagRules.FindAsync(id);
            if (rule == null)
                return Results.NotFound(new { success = false, message = "Rule not found" });

            context.ApprovedTagRules.Remove(rule);
            await context.SaveChangesAsync();

            return Results.Ok(new { 
                success = true, 
                message = "Rule deleted successfully",
                ruleId = rule.Id
            });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Error deleting rule: {ex.Message}");
        }
    }

    private static async Task<IResult> CreateManualRule(HttpContext httpContext, JumpChainDbContext context, AdminAuthService authService, CreateManualRuleRequest request)
    {
        var (valid, user) = await ValidateSession(httpContext, authService);
        if (!valid)
            return Results.Unauthorized();

        try
        {
            // Find the document
            var document = await context.JumpDocuments
                .FirstOrDefaultAsync(d => d.GoogleDriveFileId == request.GoogleDriveFileId);

            if (document == null)
                return Results.NotFound(new { success = false, message = "Document not found" });

            // Check for duplicate rule
            var existingRule = await context.ApprovedTagRules
                .FirstOrDefaultAsync(r => r.GoogleDriveFileId == request.GoogleDriveFileId &&
                                        r.TagName == request.TagName &&
                                        r.TagCategory == request.TagCategory &&
                                        r.RuleType == request.RuleType &&
                                        r.IsActive);

            if (existingRule != null)
                return Results.BadRequest(new { success = false, message = "An active rule with these parameters already exists" });

            // Create the rule
            var rule = new ApprovedTagRule
            {
                GoogleDriveFileId = request.GoogleDriveFileId,
                DocumentName = document.Name,
                TagName = request.TagName,
                TagCategory = request.TagCategory,
                RuleType = request.RuleType,
                ApprovalSource = "ManualOverride",
                ApprovedByUserId = user?.Username ?? "admin",
                IsActive = true,
                Notes = request.Notes,
                CreatedAt = DateTime.UtcNow
            };

            context.ApprovedTagRules.Add(rule);
            await context.SaveChangesAsync();

            return Results.Ok(new { 
                success = true, 
                message = "Manual rule created successfully",
                ruleId = rule.Id,
                rule = new {
                    rule.Id,
                    rule.GoogleDriveFileId,
                    rule.DocumentName,
                    rule.TagName,
                    rule.TagCategory,
                    rule.RuleType,
                    rule.IsActive,
                    rule.Notes
                }
            });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Error creating manual rule: {ex.Message}");
        }
    }

    // Helper method to validate session
    private static async Task<(bool valid, AdminUser? user)> ValidateSession(HttpContext context, AdminAuthService authService)
    {
        var sessionToken = context.Request.Cookies["admin_session"];
        if (string.IsNullOrEmpty(sessionToken))
        {
            return (false, null);
        }

        return await authService.ValidateSessionAsync(sessionToken);
    }
}

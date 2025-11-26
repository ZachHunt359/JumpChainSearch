using JumpChainSearch.Data;
using JumpChainSearch.Models;
using Microsoft.EntityFrameworkCore;

namespace JumpChainSearch.Services;

/// <summary>
/// Service for managing and applying ApprovedTagRule records.
/// Handles persistent tag rules that survive bulk regeneration operations.
/// </summary>
public class TagRuleService
{
    private readonly JumpChainDbContext _context;
    
    public TagRuleService(JumpChainDbContext context)
    {
        _context = context;
    }
    
    /// <summary>
    /// Applies all active ApprovedTagRules to documents.
    /// This restores community-voted tag changes after bulk operations.
    /// </summary>
    /// <param name="categoryFilter">Optional category filter (e.g., "Genre", "Series")</param>
    /// <returns>Statistics about rules applied</returns>
    public async Task<ApplyRulesResult> ApplyApprovedRules(string? categoryFilter = null)
    {
        var query = _context.ApprovedTagRules
            .Where(r => r.IsActive);
            
        if (!string.IsNullOrEmpty(categoryFilter))
        {
            query = query.Where(r => r.TagCategory == categoryFilter);
        }
        
        var rules = await query.ToListAsync();
        
        int additionsApplied = 0;
        int removalsApplied = 0;
        int notFound = 0;
        var appliedRuleIds = new List<int>();

        foreach (var rule in rules)
        {
            // Find the document by GoogleDriveFileId
            var document = await _context.JumpDocuments
                .FirstOrDefaultAsync(d => d.GoogleDriveFileId == rule.GoogleDriveFileId);

            if (document == null)
            {
                notFound++;
                continue;
            }

            if (rule.RuleType == "Add")
            {
                // Check if tag already exists
                var existingTag = await _context.DocumentTags
                    .FirstOrDefaultAsync(t => t.JumpDocumentId == document.Id && 
                                            t.TagName == rule.TagName && 
                                            t.TagCategory == rule.TagCategory);

                if (existingTag == null)
                {
                    // Add the tag
                    _context.DocumentTags.Add(new DocumentTag
                    {
                        JumpDocumentId = document.Id,
                        TagName = rule.TagName,
                        TagCategory = rule.TagCategory
                    });

                    additionsApplied++;
                    appliedRuleIds.Add(rule.Id);
                }
            }
            else if (rule.RuleType == "Remove")
            {
                // Find and remove the tag
                var tagToRemove = await _context.DocumentTags
                    .FirstOrDefaultAsync(t => t.JumpDocumentId == document.Id && 
                                            t.TagName == rule.TagName && 
                                            t.TagCategory == rule.TagCategory);

                if (tagToRemove != null)
                {
                    _context.DocumentTags.Remove(tagToRemove);
                    removalsApplied++;
                    appliedRuleIds.Add(rule.Id);
                }
            }

            // Update rule metadata
            rule.LastAppliedAt = DateTime.UtcNow;
            rule.TimesApplied++;
        }

        await _context.SaveChangesAsync();

        return new ApplyRulesResult
        {
            TotalRules = rules.Count,
            AdditionsApplied = additionsApplied,
            RemovalsApplied = removalsApplied,
            DocumentsNotFound = notFound,
            AppliedRuleIds = appliedRuleIds
        };
    }
}

/// <summary>
/// Result of applying ApprovedTagRules
/// </summary>
public class ApplyRulesResult
{
    public int TotalRules { get; set; }
    public int AdditionsApplied { get; set; }
    public int RemovalsApplied { get; set; }
    public int DocumentsNotFound { get; set; }
    public List<int> AppliedRuleIds { get; set; } = new();
}

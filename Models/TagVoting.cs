using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace JumpChainSearch.Models
{
    /// <summary>
    /// Represents a user's suggestion to add a new tag to a document
    /// </summary>
    public class TagSuggestion
    {
        public int Id { get; set; }
        
        public int JumpDocumentId { get; set; }
        
        [Required]
        public string TagName { get; set; } = string.Empty;
        
        [Required]
        public string TagCategory { get; set; } = string.Empty;
        
        /// <summary>
        /// User identifier (session ID, IP hash, or username if authenticated)
        /// </summary>
        [Required]
        public string SuggestedByUserId { get; set; } = string.Empty;
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        /// <summary>
        /// Status: Pending, Approved, Rejected, Applied
        /// </summary>
        public string Status { get; set; } = "Pending";
        
        /// <summary>
        /// When the tag was actually applied to the document
        /// </summary>
        public DateTime? AppliedAt { get; set; }
        
        /// <summary>
        /// Optional reason for admin rejection
        /// </summary>
        public string? RejectionReason { get; set; }
        
        // Navigation properties
        public virtual JumpDocument JumpDocument { get; set; } = null!;
        public virtual ICollection<TagVote> Votes { get; set; } = new List<TagVote>();
    }

    /// <summary>
    /// Represents a user's request to remove an existing tag from a document
    /// </summary>
    public class TagRemovalRequest
    {
        public int Id { get; set; }
        
        public int JumpDocumentId { get; set; }
        
        public int? DocumentTagId { get; set; }
        
        [Required]
        public string TagName { get; set; } = string.Empty;
        
        [Required]
        public string TagCategory { get; set; } = string.Empty;
        
        /// <summary>
        /// User identifier who requested the removal
        /// </summary>
        [Required]
        public string RequestedByUserId { get; set; } = string.Empty;
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        /// <summary>
        /// Status: Pending, Approved, Rejected, Removed
        /// </summary>
        public string Status { get; set; } = "Pending";
        
        /// <summary>
        /// When the tag was actually removed from the document
        /// </summary>
        public DateTime? RemovedAt { get; set; }
        
        /// <summary>
        /// Optional reason for admin rejection
        /// </summary>
        public string? RejectionReason { get; set; }
        
        // Navigation properties
        public virtual JumpDocument JumpDocument { get; set; } = null!;
        public virtual DocumentTag? DocumentTag { get; set; }
        public virtual ICollection<TagVote> Votes { get; set; } = new List<TagVote>();
    }

    /// <summary>
    /// Represents a user's vote on a tag suggestion or removal request
    /// </summary>
    public class TagVote
    {
        public int Id { get; set; }
        
        /// <summary>
        /// User identifier (session ID, IP hash, or username if authenticated)
        /// </summary>
        [Required]
        public string UserId { get; set; } = string.Empty;
        
        /// <summary>
        /// FK to TagSuggestion (nullable - only set if voting on a suggestion)
        /// </summary>
        public int? TagSuggestionId { get; set; }
        
        /// <summary>
        /// FK to TagRemovalRequest (nullable - only set if voting on a removal)
        /// </summary>
        public int? TagRemovalRequestId { get; set; }
        
        /// <summary>
        /// True = vote for the change (add/remove), False = vote against
        /// </summary>
        public bool IsInFavor { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        /// <summary>
        /// Weight of the vote (1.0 = full weight, decreases over time)
        /// </summary>
        public double Weight { get; set; } = 1.0;
        
        // Navigation properties
        public virtual TagSuggestion? TagSuggestion { get; set; }
        public virtual TagRemovalRequest? TagRemovalRequest { get; set; }
    }

    /// <summary>
    /// Stores user-specific tag overrides (in localStorage on client, mirrored in DB for analytics)
    /// </summary>
    public class UserTagOverride
    {
        public int Id { get; set; }
        
        [Required]
        public string UserId { get; set; } = string.Empty;
        
        public int JumpDocumentId { get; set; }
        
        [Required]
        public string TagName { get; set; } = string.Empty;
        
        [Required]
        public string TagCategory { get; set; } = string.Empty;
        
        /// <summary>
        /// True = user added this tag for themselves, False = user removed it
        /// </summary>
        public bool IsAdded { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        // Navigation property
        public virtual JumpDocument JumpDocument { get; set; } = null!;
    }

    /// <summary>
    /// Configuration for voting thresholds and rules
    /// </summary>
    public class VotingConfiguration
    {
        public int Id { get; set; }
        
        /// <summary>
        /// Minimum number of total votes required
        /// </summary>
        public int MinimumVotesRequired { get; set; } = 50;
        
        /// <summary>
        /// Percentage (0-100) of votes that must be in favor
        /// </summary>
        public double RequiredAgreementPercentage { get; set; } = 70.0;
        
        /// <summary>
        /// Whether to scale minimum votes based on document popularity
        /// </summary>
        public bool ScaleByPopularity { get; set; } = true;
        
        /// <summary>
        /// Multiplier for popularity-based scaling (e.g., 0.05 = 5% of views)
        /// </summary>
        public double PopularityScaleFactor { get; set; } = 0.05;
        
        /// <summary>
        /// Maximum votes required even for very popular documents
        /// </summary>
        public int MaximumVotesRequired { get; set; } = 200;
        
        /// <summary>
        /// Days before vote weight starts to decay
        /// </summary>
        public int VoteDecayStartDays { get; set; } = 90;
        
        /// <summary>
        /// Rate of vote weight decay per day after decay start
        /// </summary>
        public double VoteDecayRatePerDay { get; set; } = 0.01;
        
        /// <summary>
        /// Whether auto-application is enabled
        /// </summary>
        public bool AutoApplyEnabled { get; set; } = true;
        
        public DateTime LastModified { get; set; } = DateTime.UtcNow;
        
        [Required]
        public string ModifiedBy { get; set; } = "System";
    }

    /// <summary>
    /// Tracks document view counts for popularity-based voting thresholds
    /// </summary>
    public class DocumentViewCount
    {
        public int Id { get; set; }
        
        public int JumpDocumentId { get; set; }
        
        /// <summary>
        /// Total number of times document details were viewed
        /// </summary>
        public int ViewCount { get; set; } = 0;
        
        /// <summary>
        /// Unique users who viewed (based on session/IP)
        /// </summary>
        public int UniqueViewCount { get; set; } = 0;
        
        public DateTime LastViewed { get; set; } = DateTime.UtcNow;
        
        // Navigation property
        public virtual JumpDocument JumpDocument { get; set; } = null!;
    }

    /// <summary>
    /// Persistent record of approved tag modifications that should be reapplied after tag regeneration
    /// </summary>
    public class ApprovedTagRule
    {
        public int Id { get; set; }
        
        /// <summary>
        /// Document identifier - using GoogleDriveFileId since it's stable across regenerations
        /// </summary>
        [Required]
        public string GoogleDriveFileId { get; set; } = string.Empty;
        
        /// <summary>
        /// Document name at time of approval (for reference/logging)
        /// </summary>
        [Required]
        public string DocumentName { get; set; } = string.Empty;
        
        [Required]
        public string TagName { get; set; } = string.Empty;
        
        [Required]
        public string TagCategory { get; set; } = string.Empty;
        
        /// <summary>
        /// Type of rule: "Add" or "Remove"
        /// </summary>
        [Required]
        public string RuleType { get; set; } = string.Empty; // "Add" or "Remove"
        
        /// <summary>
        /// How this rule was created: "CommunityVote", "AdminApproval", "ManualOverride"
        /// </summary>
        [Required]
        public string ApprovalSource { get; set; } = string.Empty;
        
        /// <summary>
        /// User ID who approved (if admin) or initiated the suggestion
        /// </summary>
        public string? ApprovedByUserId { get; set; }
        
        /// <summary>
        /// When this rule was created/approved
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        /// <summary>
        /// Reference to original suggestion (if from voting system)
        /// </summary>
        public int? TagSuggestionId { get; set; }
        
        /// <summary>
        /// Reference to original removal request (if from voting system)
        /// </summary>
        public int? TagRemovalRequestId { get; set; }
        
        /// <summary>
        /// Number of votes in favor at time of approval (for audit trail)
        /// </summary>
        public int? VotesInFavor { get; set; }
        
        /// <summary>
        /// Total votes at time of approval (for audit trail)
        /// </summary>
        public int? TotalVotes { get; set; }
        
        /// <summary>
        /// Optional notes/reason for the rule
        /// </summary>
        public string? Notes { get; set; }
        
        /// <summary>
        /// Whether this rule is currently active (can be disabled without deletion)
        /// </summary>
        public bool IsActive { get; set; } = true;
        
        /// <summary>
        /// Last time this rule was applied during tag regeneration
        /// </summary>
        public DateTime? LastAppliedAt { get; set; }
        
        /// <summary>
        /// How many times this rule has been applied
        /// </summary>
        public int TimesApplied { get; set; } = 0;
        
        // Navigation properties
        public virtual TagSuggestion? TagSuggestion { get; set; }
        public virtual TagRemovalRequest? TagRemovalRequest { get; set; }
    }
}

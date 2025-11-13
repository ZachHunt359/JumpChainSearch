using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace JumpChainSearch.Models
{
    public class JumpDocument
    {
        public int Id { get; set; }
        
        [Required]
        public string GoogleDriveFileId { get; set; } = string.Empty;
        
        [Required]
        public string Name { get; set; } = string.Empty;
        
        public string Description { get; set; } = string.Empty;
        
        [Required]
        public string MimeType { get; set; } = string.Empty;
        
        public long Size { get; set; }
        
        public DateTime CreatedTime { get; set; }
        
        public DateTime ModifiedTime { get; set; }
        
        public DateTime LastScanned { get; set; }
        
        public DateTime LastModified { get; set; }
        
        [Required]
        public string SourceDrive { get; set; } = string.Empty;
        
        public string FolderPath { get; set; } = string.Empty;
        
        public string WebViewLink { get; set; } = string.Empty;
        
        public string DownloadLink { get; set; } = string.Empty;
        
        // Thumbnail information
        public string ThumbnailLink { get; set; } = string.Empty;
        
        public bool HasThumbnail { get; set; }
        
        // Full text content extracted from the document
        public string? ExtractedText { get; set; }
        
        // Method used to extract text (e.g., "google_drive_export", "basic_pdfpig", "improved_pdfpig")
        public string? ExtractionMethod { get; set; }
        
        // Text review/editing fields
        public bool TextNeedsReview { get; set; }
        public DateTime? TextReviewFlaggedAt { get; set; }
        public string? TextReviewFlaggedBy { get; set; }
        public DateTime? TextLastEditedAt { get; set; }
        public string? TextLastEditedBy { get; set; }
        
        // Navigation properties
        public virtual ICollection<DocumentTag> Tags { get; set; } = new List<DocumentTag>();
        public virtual ICollection<DocumentUrl> Urls { get; set; } = new List<DocumentUrl>();
        public virtual ICollection<DocumentPurchasable> Purchasables { get; set; } = new List<DocumentPurchasable>();
    }
    
    public class DocumentUrl
    {
        public int Id { get; set; }
        
        public int JumpDocumentId { get; set; }
        
        [Required]
        public string GoogleDriveFileId { get; set; } = string.Empty;
        
        [Required]
        public string SourceDrive { get; set; } = string.Empty;
        
        public string FolderPath { get; set; } = string.Empty;
        
        public string WebViewLink { get; set; } = string.Empty;
        
        public string DownloadLink { get; set; } = string.Empty;
        
        public DateTime LastScanned { get; set; }
        
        // Additional properties for URL management
        public string Url { get; set; } = string.Empty;
        
        public bool IsDownloadUrl { get; set; }
        
        public bool IsPublicUrl { get; set; }
        
        // Navigation properties
        public virtual JumpDocument JumpDocument { get; set; } = null!;
    }
    
    public class DocumentTag
    {
        public int Id { get; set; }
        
        public int JumpDocumentId { get; set; }
        
        [Required]
        public string TagName { get; set; } = string.Empty;
        
        public string TagCategory { get; set; } = string.Empty; // e.g., "Drive", "Folder", "Type", "Custom"
        
        // Navigation properties
        public virtual JumpDocument JumpDocument { get; set; } = null!;
    }
    
    

    public class DocumentPurchasable
    {
        public int Id { get; set; }
        
        [Required]
        public int JumpDocumentId { get; set; }
        
        [Required]
        public string Name { get; set; } = string.Empty;
        
        [Required]
        public string Category { get; set; } = string.Empty; // Perks, Items, Drawbacks, Superpowers, Ship Building, etc.
        
        public string Description { get; set; } = string.Empty;
        
        // Store multiple costs as JSON or separate table
        public string CostsJson { get; set; } = string.Empty; // JSON array of cost objects
        
        // For easy searching and filtering
        public int? PrimaryCost { get; set; } // The main/first cost value
        
        // Position in the document for reference
        public int? LineNumber { get; set; }
        public int? CharacterPosition { get; set; }
        
        // Metadata
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        
        // Navigation properties
        [ForeignKey("JumpDocumentId")]
        public virtual JumpDocument JumpDocument { get; set; } = null!;
    }

    public class PurchasableCost
    {
        public int Value { get; set; }
        public string Currency { get; set; } = "CP"; // CP, Points, etc.
        public string? Tier { get; set; } // "Basic", "Advanced", "Master", etc.
        public string? Description { get; set; } // Additional context for this cost tier
    }
}
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace JumpChainSearch.Models
{
    /// <summary>
    /// Represents a folder within a Google Drive, with hierarchical parent-child relationships.
    /// Stores resource keys and preferred authentication methods for folder-level access control.
    /// </summary>
    public class FolderConfiguration
    {
        public int Id { get; set; }

        /// <summary>
        /// Google Drive folder ID
        /// </summary>
        [Required]
        public string FolderId { get; set; } = string.Empty;

        /// <summary>
        /// Human-readable folder name (e.g., "Complete Jumps - ABCDE")
        /// </summary>
        [Required]
        public string FolderName { get; set; } = string.Empty;

        /// <summary>
        /// Foreign key to parent DriveConfiguration
        /// </summary>
        [Required]
        public int ParentDriveId { get; set; }

        /// <summary>
        /// Navigation property to parent drive
        /// </summary>
        [ForeignKey("ParentDriveId")]
        public DriveConfiguration? ParentDrive { get; set; }

        /// <summary>
        /// Resource key for link-shared folders requiring additional authentication.
        /// Extracted from URLs like: drive.google.com/drive/folders/[FolderId]?resourcekey=[ResourceKey]
        /// </summary>
        public string? ResourceKey { get; set; }

        /// <summary>
        /// Preferred authentication method: "ServiceAccount", "ApiKey", or null (auto-detect).
        /// Learned from successful scan attempts.
        /// </summary>
        public string? PreferredAuthMethod { get; set; }

        /// <summary>
        /// Full path in the drive hierarchy (e.g., "/SB Drive/Complete Jumps")
        /// </summary>
        public string FolderPath { get; set; } = string.Empty;

        /// <summary>
        /// Number of documents directly in this folder (not recursive)
        /// </summary>
        public int DocumentCount { get; set; }

        /// <summary>
        /// Last time this folder was scanned
        /// </summary>
        public DateTime LastScanTime { get; set; }

        /// <summary>
        /// Whether this folder should be included in scans
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Optional description or notes about this folder
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Tracks if this folder was auto-discovered during a scan vs manually configured
        /// </summary>
        public bool IsAutoDiscovered { get; set; } = false;

        /// <summary>
        /// When this folder configuration was first created
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last time metadata was updated
        /// </summary>
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}

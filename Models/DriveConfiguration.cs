using System;
using System.ComponentModel.DataAnnotations;

namespace JumpChainSearch.Models
{
    public class DriveConfiguration
    {
        public int Id { get; set; }

        [Required]
        public string DriveId { get; set; } = string.Empty;

        [Required]
        public string DriveName { get; set; } = string.Empty;

        public string? Description { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime LastScanTime { get; set; }

        public int DocumentCount { get; set; }

        // Resource key for link-shared folders requiring additional authentication
        public string? ResourceKey { get; set; }

        // For subfolders: the parent drive name to use for tagging (e.g., "SB Drive")
        // If null, uses DriveName for tags. Allows subfolder scanning while preserving parent drive identity.
        public string? ParentDriveName { get; set; }
    }
}
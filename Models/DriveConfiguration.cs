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
    }
}
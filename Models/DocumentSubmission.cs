using System.ComponentModel.DataAnnotations;

namespace JumpChainSearch.Models;

public sealed class DocumentSubmission
{
    public int Id { get; set; }

    [Required]
    public string DocumentUrl { get; set; } = string.Empty;

    [Required]
    public string GoogleDriveFileId { get; set; } = string.Empty;

    public string? ResourceKey { get; set; }
    public string? Notes { get; set; }
    public string? SubmitterName { get; set; }

    [Required]
    public string Status { get; set; } = "Pending";

    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedBy { get; set; }
    public string? ReviewNotes { get; set; }
    public int? JumpDocumentId { get; set; }
    public JumpDocument? JumpDocument { get; set; }
}
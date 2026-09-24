namespace JumpChainSearch.DTOs;

public sealed record CreateDocumentSubmissionRequest(
    string DocumentUrl,
    string? Notes,
    string? SubmitterName);

public sealed record ReviewDocumentSubmissionRequest(string? ReviewNotes);
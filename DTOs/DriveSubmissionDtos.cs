namespace JumpChainSearch.DTOs;

public sealed record CreateDriveSubmissionRequest(
    string DriveUrl,
    string SuggestedName,
    string? Notes,
    string? SubmitterName);

public sealed record SaveDriveConfigurationRequest(
    string DriveUrl,
    string DriveName,
    string? Description,
    string? ParentDriveName,
    string? PreferredAuthMethod,
    bool IsActive);

public sealed record ReviewDriveSubmissionRequest(
    string? DriveName,
    string? Description,
    string? ParentDriveName,
    string? PreferredAuthMethod,
    string? ReviewNotes);
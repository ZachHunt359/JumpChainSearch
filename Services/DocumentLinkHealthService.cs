namespace JumpChainSearch.Services;

public enum DocumentLinkHealth
{
    Healthy,
    Dead,
    Unknown
}

public sealed record DocumentLinkVerificationResult(
    DocumentLinkHealth Health,
    string Message,
    string? Name = null,
    string? MimeType = null,
    long? Size = null,
    string? WebViewLink = null);

public interface IDocumentLinkHealthService
{
    Task<DocumentLinkVerificationResult> VerifyPublicLinkAsync(
        string fileId,
        string? resourceKey,
        CancellationToken cancellationToken = default);
}

public sealed class DocumentLinkHealthService : IDocumentLinkHealthService
{
    private readonly IGoogleDriveService _googleDriveService;

    public DocumentLinkHealthService(IGoogleDriveService googleDriveService)
    {
        _googleDriveService = googleDriveService;
    }

    public Task<DocumentLinkVerificationResult> VerifyPublicLinkAsync(
        string fileId,
        string? resourceKey,
        CancellationToken cancellationToken = default)
        => _googleDriveService.VerifyFileAccessAsync(fileId, resourceKey, cancellationToken);
}

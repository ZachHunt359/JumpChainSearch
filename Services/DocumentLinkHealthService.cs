using System.Net;
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;

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
    private static readonly HashSet<string> TransientReasons = new(StringComparer.OrdinalIgnoreCase)
    {
        "dailyLimitExceeded",
        "rateLimitExceeded",
        "userRateLimitExceeded",
        "sharingRateLimitExceeded"
    };

    private readonly IReadOnlyList<DriveService> _driveServices;
    private readonly ILogger<DocumentLinkHealthService> _logger;

    public DocumentLinkHealthService(IConfiguration configuration, ILogger<DocumentLinkHealthService> logger)
    {
        _logger = logger;
        var services = new List<DriveService>
        {
            new(new BaseClientService.Initializer
            {
                ApiKey = configuration["GOOGLE_API_KEY"]
                    ?? Environment.GetEnvironmentVariable("GOOGLE_API_KEY")
                    ?? string.Empty,
                ApplicationName = "JumpChain Search - Link Verification"
            })
        };

        try
        {
            var credential = CreateCredential(configuration);
            if (credential != null)
            {
                services.Add(new DriveService(new BaseClientService.Initializer
                {
                    HttpClientInitializer = credential,
                    ApplicationName = "JumpChain Search - Authenticated Link Verification"
                }));
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Authenticated link verification is unavailable; public verification will still be used");
        }

        _driveServices = services;
    }

    public async Task<DocumentLinkVerificationResult> VerifyPublicLinkAsync(
        string fileId,
        string? resourceKey,
        CancellationToken cancellationToken = default)
    {
        var sawTransientFailure = false;

        foreach (var service in _driveServices)
        {
            try
            {
                var request = service.Files.Get(fileId);
                request.SupportsAllDrives = true;
                request.Fields = "id,name,mimeType,size,trashed,webViewLink";
                if (!string.IsNullOrWhiteSpace(resourceKey))
                {
                    request.ModifyRequest += message => message.Headers.TryAddWithoutValidation(
                        "X-Goog-Drive-Resource-Keys",
                        $"{fileId}/{resourceKey}");
                }

                var file = await request.ExecuteAsync(cancellationToken);
                if (file.Trashed == true)
                    return new(DocumentLinkHealth.Dead, "Google Drive reports that the file is in the trash.");

                return new(
                    DocumentLinkHealth.Healthy,
                    "Google Drive confirmed that the file is accessible.",
                    file.Name,
                    file.MimeType,
                    file.Size,
                    file.WebViewLink);
            }
            catch (GoogleApiException exception) when (IsDefinitiveAccessFailure(exception))
            {
                continue;
            }
            catch (Exception exception)
            {
                sawTransientFailure = true;
                _logger.LogWarning(exception, "Google Drive could not conclusively verify file {FileId}", fileId);
            }
        }

        return sawTransientFailure
            ? new(DocumentLinkHealth.Unknown, "Google Drive could not verify this link right now. Its existing status was not changed.")
            : new(DocumentLinkHealth.Dead, "Google Drive reports that the file is missing or inaccessible.");
    }

    private static GoogleCredential? CreateCredential(IConfiguration configuration)
    {
        var credentialsPath = configuration["GOOGLE_APPLICATION_CREDENTIALS"]
            ?? Environment.GetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS");
        if (!string.IsNullOrWhiteSpace(credentialsPath))
        {
            var resolvedPath = Path.IsPathRooted(credentialsPath)
                ? credentialsPath
                : Path.Combine(Directory.GetCurrentDirectory(), credentialsPath);
            if (File.Exists(resolvedPath))
            {
                return GoogleCredential.FromFile(resolvedPath)
                    .CreateScoped(DriveService.Scope.DriveReadonly);
            }
        }

        var serviceAccountJson = configuration["GOOGLE_DRIVE_SERVICE_ACCOUNT_KEY"]
            ?? Environment.GetEnvironmentVariable("GOOGLE_DRIVE_SERVICE_ACCOUNT_KEY");
        return string.IsNullOrWhiteSpace(serviceAccountJson)
            ? null
            : GoogleCredential.FromJson(serviceAccountJson)
                .CreateScoped(DriveService.Scope.DriveReadonly);
    }

    private static bool IsDefinitiveAccessFailure(GoogleApiException exception)
    {
        if (exception.HttpStatusCode == HttpStatusCode.NotFound)
            return true;

        if (exception.HttpStatusCode != HttpStatusCode.Forbidden)
            return false;

        var reasons = exception.Error?.Errors?
            .Select(error => error.Reason)
            .Where(reason => !string.IsNullOrWhiteSpace(reason))
            .ToList() ?? [];

        return reasons.Count == 0 || reasons.All(reason => !TransientReasons.Contains(reason));
    }
}

using JumpChainSearch.Data;
using JumpChainSearch.DTOs;
using JumpChainSearch.Models;
using JumpChainSearch.Services;
using Microsoft.EntityFrameworkCore;

namespace JumpChainSearch.Extensions;

public static class DocumentLinkEndpoints
{
    public static RouteGroupBuilder MapDocumentLinkEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/{linkId:int}/report", ReportLink)
            .WithSummary("Report and verify a Google Drive document link")
            .WithDescription("Checks the link with Google Drive and records the verified result instead of trusting the reported state.");

        return group;
    }

    private static async Task<IResult> ReportLink(
        int linkId,
        ReportDocumentLinkRequest request,
        JumpChainDbContext context,
        IDocumentLinkHealthService healthService,
        SearchCacheInvalidationService cacheInvalidation,
        CancellationToken cancellationToken)
    {
        var link = await context.DocumentUrls
            .Include(documentUrl => documentUrl.JumpDocument)
                .ThenInclude(document => document.Tags)
            .Include(documentUrl => documentUrl.JumpDocument)
                .ThenInclude(document => document.Urls)
            .FirstOrDefaultAsync(documentUrl => documentUrl.Id == linkId, cancellationToken);

        if (link == null)
            return Results.NotFound(new { success = false, message = "Document link not found." });

        var verification = await healthService.VerifyPublicLinkAsync(
            link.GoogleDriveFileId,
            link.ResourceKey,
            cancellationToken);

        var checkedAt = DateTime.UtcNow;
        link.LastReportedAt = checkedAt;
        link.LastHealthCheckAt = checkedAt;
        link.LastHealthCheckStatus = verification.Health.ToString();
        link.LastHealthCheckMessage = verification.Message;

        if (verification.Health == DocumentLinkHealth.Unknown)
        {
            await context.SaveChangesAsync(cancellationToken);
            return Results.Json(new
            {
                success = false,
                verified = false,
                isDead = link.IsDead,
                message = verification.Message
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        link.IsDead = verification.Health == DocumentLinkHealth.Dead;
        SynchronizeDocumentAvailability(link.JumpDocument);
        await context.SaveChangesAsync(cancellationToken);
        cacheInvalidation.InvalidateAllSearchCaches();

        var reportMatched = request.ReportedDead == link.IsDead;
        return Results.Ok(new
        {
            success = true,
            verified = true,
            link.Id,
            link.IsDead,
            reportMatched,
            message = reportMatched
                ? verification.Message
                : $"Your report was recorded, but verification found the link to be {(link.IsDead ? "dead" : "healthy")}."
        });
    }

    public static void SynchronizeDocumentAvailability(JumpDocument document)
    {
        var healthyLinks = document.Urls
            .Where(link => !link.IsDead)
            .OrderByDescending(link => link.GoogleDriveFileId == document.GoogleDriveFileId)
            .ThenByDescending(link => link.LastHealthCheckAt)
            .ThenBy(link => link.Id)
            .ToList();

        var deadLinkTag = document.Tags.FirstOrDefault(tag =>
            tag.TagName.Equals("Dead Link", StringComparison.OrdinalIgnoreCase));

        if (healthyLinks.Count == 0)
        {
            if (deadLinkTag == null)
            {
                document.Tags.Add(new DocumentTag
                {
                    TagName = "Dead Link",
                    TagCategory = "Status"
                });
            }
            return;
        }

        if (deadLinkTag != null)
            document.Tags.Remove(deadLinkTag);

        var primary = healthyLinks[0];
        document.GoogleDriveFileId = primary.GoogleDriveFileId;
        document.SourceDrive = primary.SourceDrive;
        document.FolderPath = primary.FolderPath;
        document.WebViewLink = primary.WebViewLink;
        document.DownloadLink = primary.DownloadLink;
    }

    public static async Task VerifySourcesAsync(
        IEnumerable<DocumentUrl> sources,
        IDocumentLinkHealthService healthService,
        CancellationToken cancellationToken = default)
    {
        foreach (var source in sources.DistinctBy(link => link.GoogleDriveFileId))
        {
            var verification = await healthService.VerifyPublicLinkAsync(
                source.GoogleDriveFileId,
                source.ResourceKey,
                cancellationToken);
            source.LastHealthCheckAt = DateTime.UtcNow;
            source.LastHealthCheckStatus = verification.Health.ToString();
            source.LastHealthCheckMessage = verification.Message;

            if (verification.Health != DocumentLinkHealth.Unknown)
                source.IsDead = verification.Health == DocumentLinkHealth.Dead;
        }
    }
}

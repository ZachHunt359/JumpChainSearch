using JumpChainSearch.Data;
using JumpChainSearch.DTOs;
using JumpChainSearch.Helpers;
using JumpChainSearch.Models;
using JumpChainSearch.Services;
using Microsoft.EntityFrameworkCore;

namespace JumpChainSearch.Extensions;

public static class DocumentSubmissionEndpoints
{
    public static RouteGroupBuilder MapDocumentSubmissionEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/", CreateSubmission)
            .WithSummary("Submit a Google Drive document for administrator review");

        return group;
    }

    private static async Task<IResult> CreateSubmission(
        CreateDocumentSubmissionRequest request,
        JumpChainDbContext context,
        IGoogleDriveService driveService,
        DocumentDuplicateDetectionService duplicateDetection,
        CancellationToken cancellationToken)
    {
        var documentUrl = request.DocumentUrl?.Trim();
        if (documentUrl?.Length > 1000
            || !GoogleDriveFileLinkParser.TryParse(documentUrl, out var link)
            || link == null)
        {
            return Results.BadRequest(new
            {
                success = false,
                message = "Enter a valid Google Drive document URL."
            });
        }

        if (request.Notes?.Length > 2000 || request.SubmitterName?.Length > 100)
        {
            return Results.BadRequest(new
            {
                success = false,
                message = "Submission details exceed the allowed length."
            });
        }

        var existingSource = await duplicateDetection.FindByDriveFileIdAsync(link.FileId, cancellationToken);
        if (existingSource != null)
        {
            return Results.Conflict(new
            {
                success = false,
                documentId = existingSource.DocumentId,
                message = $"That Google Drive file is already indexed as {existingSource.DocumentName}."
            });
        }

        if (await context.DocumentSubmissions.AnyAsync(submission =>
                submission.GoogleDriveFileId == link.FileId && submission.Status == "Pending",
                cancellationToken))
        {
            return Results.Conflict(new
            {
                success = false,
                message = "That document is already awaiting review."
            });
        }

        JumpDocument submittedDocument;
        try
        {
            submittedDocument = await driveService.GetSubmittedDocumentAsync(link.FileId, link.ResourceKey);
        }
        catch (InvalidOperationException exception)
        {
            return Results.BadRequest(new { success = false, message = exception.Message });
        }

        var contentMatch = await duplicateDetection.FindByContentAsync(submittedDocument, cancellationToken);

        var submission = new DocumentSubmission
        {
            DocumentUrl = documentUrl!,
            GoogleDriveFileId = link.FileId,
            ResourceKey = link.ResourceKey,
            Notes = NormalizeOptional(request.Notes),
            SubmitterName = NormalizeOptional(request.SubmitterName),
            JumpDocumentId = contentMatch?.DocumentId,
            DuplicateReason = contentMatch == null
                ? null
                : DocumentDuplicateDetectionService.DescribeMatch(contentMatch)
        };

        context.DocumentSubmissions.Add(submission);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Results.Conflict(new
            {
                success = false,
                message = "That document is already awaiting review."
            });
        }

        return Results.Created($"/api/document-submissions/{submission.Id}", new
        {
            success = true,
            submissionId = submission.Id,
            potentialDuplicate = contentMatch != null,
            matchedDocumentId = contentMatch?.DocumentId,
            message = contentMatch == null
                ? "Document submitted for administrator review."
                : $"This file matches {contentMatch.DocumentName} and was submitted for review as an additional source."
        });
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }
}
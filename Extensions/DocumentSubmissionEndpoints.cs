using JumpChainSearch.Data;
using JumpChainSearch.DTOs;
using JumpChainSearch.Helpers;
using JumpChainSearch.Models;
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
        JumpChainDbContext context)
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

        if (await context.JumpDocuments.AnyAsync(document => document.GoogleDriveFileId == link.FileId))
        {
            return Results.Conflict(new
            {
                success = false,
                message = "That document is already indexed."
            });
        }

        if (await context.DocumentSubmissions.AnyAsync(submission =>
                submission.GoogleDriveFileId == link.FileId && submission.Status == "Pending"))
        {
            return Results.Conflict(new
            {
                success = false,
                message = "That document is already awaiting review."
            });
        }

        var submission = new DocumentSubmission
        {
            DocumentUrl = documentUrl!,
            GoogleDriveFileId = link.FileId,
            ResourceKey = link.ResourceKey,
            Notes = NormalizeOptional(request.Notes),
            SubmitterName = NormalizeOptional(request.SubmitterName)
        };

        context.DocumentSubmissions.Add(submission);
        try
        {
            await context.SaveChangesAsync();
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
            message = "Document submitted for administrator review."
        });
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }
}
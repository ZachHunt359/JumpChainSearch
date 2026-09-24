using JumpChainSearch.Data;
using JumpChainSearch.DTOs;
using JumpChainSearch.Helpers;
using JumpChainSearch.Models;
using Microsoft.EntityFrameworkCore;

namespace JumpChainSearch.Extensions;

public static class DriveSubmissionEndpoints
{
    public static RouteGroupBuilder MapDriveSubmissionEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/", CreateSubmission)
            .WithSummary("Submit a Google Drive folder for administrator review");

        return group;
    }

    private static async Task<IResult> CreateSubmission(
        CreateDriveSubmissionRequest request,
        JumpChainDbContext context)
    {
        var driveUrl = request.DriveUrl?.Trim();
        var suggestedName = request.SuggestedName?.Trim();

        if (driveUrl?.Length > 1000 ||
            !GoogleDriveLinkParser.TryParseFolderUrl(driveUrl, out var driveId, out var resourceKey) ||
            resourceKey?.Length > 500)
        {
            return Results.BadRequest(new
            {
                success = false,
                message = "Enter a valid Google Drive folder URL."
            });
        }

        if (string.IsNullOrWhiteSpace(suggestedName) || suggestedName.Length > 200)
        {
            return Results.BadRequest(new
            {
                success = false,
                message = "Drive name is required and must be 200 characters or fewer."
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

        if (await context.DriveConfigurations.AnyAsync(drive => drive.DriveId == driveId))
        {
            return Results.Conflict(new
            {
                success = false,
                message = "That drive is already configured."
            });
        }

        if (await context.DriveSubmissions.AnyAsync(submission =>
                submission.DriveId == driveId && submission.Status == "Pending"))
        {
            return Results.Conflict(new
            {
                success = false,
                message = "That drive is already awaiting review."
            });
        }

        var submission = new DriveSubmission
        {
            DriveUrl = driveUrl!,
            DriveId = driveId,
            ResourceKey = resourceKey,
            SuggestedName = suggestedName,
            Notes = NormalizeOptional(request.Notes),
            SubmitterName = NormalizeOptional(request.SubmitterName)
        };

        context.DriveSubmissions.Add(submission);
        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return Results.Conflict(new
            {
                success = false,
                message = "That drive is already awaiting review."
            });
        }

        return Results.Created($"/api/drive-submissions/{submission.Id}", new
        {
            success = true,
            submissionId = submission.Id,
            message = "Drive submitted for administrator review."
        });
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }
}
using JumpChainSearch.Data;
using JumpChainSearch.Helpers;
using JumpChainSearch.Models;
using Microsoft.EntityFrameworkCore;

namespace JumpChainSearch.Services;

public enum DocumentDuplicateMatchKind
{
    DriveFileId,
    BinaryContent,
    ExtractedText
}

public sealed record DocumentDuplicateMatch(
    int DocumentId,
    string DocumentName,
    DocumentDuplicateMatchKind Kind);

public sealed class DocumentDuplicateDetectionService(JumpChainDbContext context)
{
    public static string DescribeMatch(DocumentDuplicateMatch match)
    {
        return match.Kind switch
        {
            DocumentDuplicateMatchKind.DriveFileId => "Google Drive file ID",
            DocumentDuplicateMatchKind.BinaryContent => "binary SHA-256",
            DocumentDuplicateMatchKind.ExtractedText => "extracted-text SHA-256",
            _ => "content fingerprint"
        };
    }

    public async Task<DocumentDuplicateMatch?> FindByDriveFileIdAsync(
        string fileId,
        CancellationToken cancellationToken = default)
    {
        var sourceMatch = await context.DocumentUrls
            .AsNoTracking()
            .Where(source => source.GoogleDriveFileId == fileId)
            .Select(source => new DocumentDuplicateMatch(
                source.JumpDocumentId,
                source.JumpDocument.Name,
                DocumentDuplicateMatchKind.DriveFileId))
            .FirstOrDefaultAsync(cancellationToken);
        if (sourceMatch != null)
            return sourceMatch;

        return await context.JumpDocuments
            .AsNoTracking()
            .Where(document => document.GoogleDriveFileId == fileId)
            .Select(document => new DocumentDuplicateMatch(
                document.Id,
                document.Name,
                DocumentDuplicateMatchKind.DriveFileId))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<DocumentDuplicateMatch?> FindByContentAsync(
        JumpDocument candidate,
        CancellationToken cancellationToken = default)
    {
        candidate.TextContentHash ??= DocumentFingerprint.FromExtractedText(candidate.ExtractedText);

        if (!string.IsNullOrWhiteSpace(candidate.BinaryContentHash))
        {
            var sourceBinaryMatch = await context.DocumentUrls
                .AsNoTracking()
                .Where(source => source.BinaryContentHash == candidate.BinaryContentHash)
                .Select(source => new DocumentDuplicateMatch(
                    source.JumpDocumentId,
                    source.JumpDocument.Name,
                    DocumentDuplicateMatchKind.BinaryContent))
                .FirstOrDefaultAsync(cancellationToken);
            if (sourceBinaryMatch != null)
                return sourceBinaryMatch;

            var binaryMatch = await context.JumpDocuments
                .AsNoTracking()
                .Where(document => document.BinaryContentHash == candidate.BinaryContentHash)
                .Select(document => new DocumentDuplicateMatch(
                    document.Id,
                    document.Name,
                    DocumentDuplicateMatchKind.BinaryContent))
                .FirstOrDefaultAsync(cancellationToken);
            if (binaryMatch != null)
                return binaryMatch;
        }

        if (string.IsNullOrWhiteSpace(candidate.TextContentHash))
            return null;

        var storedTextMatch = await context.JumpDocuments
            .AsNoTracking()
            .Where(document => document.TextContentHash == candidate.TextContentHash)
            .Select(document => new DocumentDuplicateMatch(
                document.Id,
                document.Name,
                DocumentDuplicateMatchKind.ExtractedText))
            .FirstOrDefaultAsync(cancellationToken);
        if (storedTextMatch != null)
            return storedTextMatch;

        var legacyCandidates = context.JumpDocuments
            .AsNoTracking()
            .Where(document =>
                document.TextContentHash == null &&
                document.ExtractedText != null &&
                document.MimeType == candidate.MimeType);

        legacyCandidates = candidate.Size > 0
            ? legacyCandidates.Where(document => document.Size == candidate.Size)
            : legacyCandidates.Where(document => document.Name.ToLower() == candidate.Name.Trim().ToLower());

        var candidates = await legacyCandidates
            .Select(document => new
            {
                document.Id,
                document.Name,
                document.ExtractedText
            })
            .ToListAsync(cancellationToken);

        var legacyMatch = candidates.FirstOrDefault(document =>
            DocumentFingerprint.FromExtractedText(document.ExtractedText) == candidate.TextContentHash);

        return legacyMatch == null
            ? null
            : new DocumentDuplicateMatch(
                legacyMatch.Id,
                legacyMatch.Name,
                DocumentDuplicateMatchKind.ExtractedText);
    }
}
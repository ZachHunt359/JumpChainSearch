using JumpChainSearch.Models;

namespace JumpChainSearch.Helpers;

public static class JumpDocumentScanMerge
{
    public static bool EnrichSourceLessDocument(JumpDocument existing, JumpDocument scanned)
    {
        if (!string.IsNullOrWhiteSpace(existing.SourceDrive) || string.IsNullOrWhiteSpace(scanned.SourceDrive))
        {
            return false;
        }

        existing.Name = scanned.Name;
        existing.Description = scanned.Description;
        existing.MimeType = scanned.MimeType;
        existing.Size = scanned.Size;
        existing.CreatedTime = scanned.CreatedTime;
        existing.ModifiedTime = scanned.ModifiedTime;
        existing.LastModified = scanned.LastModified == default ? scanned.ModifiedTime : scanned.LastModified;
        existing.LastScanned = DateTime.UtcNow;
        existing.SourceDrive = scanned.SourceDrive;
        existing.FolderPath = scanned.FolderPath;
        existing.WebViewLink = scanned.WebViewLink;
        existing.DownloadLink = scanned.DownloadLink;
        existing.ThumbnailLink = scanned.ThumbnailLink;
        existing.HasThumbnail = scanned.HasThumbnail;

        var driveTag = scanned.Tags.FirstOrDefault(tag => tag.TagCategory == "Drive");
        if (driveTag != null && !existing.Tags.Any(tag =>
                tag.TagName.Equals(driveTag.TagName, StringComparison.OrdinalIgnoreCase)))
        {
            existing.Tags.Add(new DocumentTag
            {
                TagName = driveTag.TagName,
                TagCategory = driveTag.TagCategory
            });
        }

        var scannedUrl = scanned.Urls.FirstOrDefault();
        if (scannedUrl != null && existing.Urls.Count == 0)
        {
            existing.Urls.Add(new DocumentUrl
            {
                GoogleDriveFileId = scannedUrl.GoogleDriveFileId,
                SourceDrive = scannedUrl.SourceDrive,
                FolderPath = scannedUrl.FolderPath,
                GoogleDriveFolderId = scannedUrl.GoogleDriveFolderId,
                ResourceKey = scannedUrl.ResourceKey,
                WebViewLink = scannedUrl.WebViewLink,
                DownloadLink = scannedUrl.DownloadLink,
                LastScanned = DateTime.UtcNow,
                IsDead = scannedUrl.IsDead,
                LastHealthCheckAt = scannedUrl.LastHealthCheckAt,
                LastHealthCheckStatus = scannedUrl.LastHealthCheckStatus,
                LastHealthCheckMessage = scannedUrl.LastHealthCheckMessage
            });
        }

        return true;
    }
}
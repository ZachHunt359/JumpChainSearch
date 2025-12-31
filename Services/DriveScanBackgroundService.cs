using JumpChainSearch.Data;
using JumpChainSearch.Models;
using Microsoft.EntityFrameworkCore;

namespace JumpChainSearch.Services;

/// <summary>
/// Background service for managing asynchronous drive scanning operations
/// </summary>
public class DriveScanBackgroundService
{
    private static bool _isScanning = false;
    private static DateTime? _scanStartTime = null;
    private static int _drivesScanned = 0;
    private static int _totalDrives = 0;
    private static int _newDocuments = 0;
    private static string? _currentDrive = null;
    private static string? _lastError = null;

    public static bool IsScanning => _isScanning;
    public static DateTime? ScanStartTime => _scanStartTime;
    public static int DrivesScanned => _drivesScanned;
    public static int TotalDrives => _totalDrives;
    public static int NewDocuments => _newDocuments;
    public static string? CurrentDrive => _currentDrive;
    public static string? LastError => _lastError;

    /// <summary>
    /// Start a background scan of all drives
    /// </summary>
    public static async Task<bool> StartScanAsync<T>(
        IServiceScopeFactory serviceScopeFactory,
        ILogger<T> logger)
    {
        if (_isScanning)
        {
            logger.LogWarning("Drive scan already in progress");
            return false;
        }

        _isScanning = true;
        _scanStartTime = DateTime.UtcNow;
        _drivesScanned = 0;
        _newDocuments = 0;
        _currentDrive = null;
        _lastError = null;

        logger.LogInformation("Starting background drive scan");

        // Start the scan in a background task with its own scope
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = serviceScopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<JumpChainDbContext>();
                var driveService = scope.ServiceProvider.GetRequiredService<IGoogleDriveService>();
                var documentCountService = scope.ServiceProvider.GetRequiredService<IDocumentCountService>();
                
                await PerformScanAsync(dbContext, driveService, documentCountService, logger);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Critical error during background drive scan");
                _lastError = ex.Message;
            }
            finally
            {
                _isScanning = false;
                _currentDrive = null;
                logger.LogInformation("Background drive scan completed");
            }
        });

        return true;
    }

    private static async Task PerformScanAsync<T>(
        JumpChainDbContext dbContext,
        IGoogleDriveService driveService,
        IDocumentCountService documentCountService,
        ILogger<T> logger)
    {
        logger.LogInformation("Starting background drive scan");

        var drives = await dbContext.DriveConfigurations
            .Where(d => d.IsActive)
            .ToListAsync();

        _totalDrives = drives.Count;

        if (drives.Count == 0)
        {
            logger.LogWarning("No active drives configured");
            _lastError = "No active drives configured";
            return;
        }

        foreach (var drive in drives)
        {
            try
            {
                _currentDrive = drive.DriveName;
                logger.LogInformation("Scanning drive: {DriveName}", drive.DriveName);

                var (documents, successfulMethod) = await driveService.ScanDriveUnifiedAsync(drive);
                var documentsList = documents.ToList();

                // Update preferred auth method if it worked
                if (successfulMethod != "None" && drive.PreferredAuthMethod != successfulMethod)
                {
                    drive.PreferredAuthMethod = successfulMethod;
                }

                // Process and save documents (simplified - full logic would match GoogleDriveEndpoints)
                var fileIds = documentsList.Select(d => d.GoogleDriveFileId).ToList();
                var existingFileIds = await dbContext.JumpDocuments
                    .Where(d => fileIds.Contains(d.GoogleDriveFileId))
                    .Select(d => d.GoogleDriveFileId)
                    .ToListAsync();

                var newDocs = 0;
                foreach (var doc in documentsList)
                {
                    if (!existingFileIds.Contains(doc.GoogleDriveFileId))
                    {
                        dbContext.JumpDocuments.Add(doc);
                        newDocs++;
                    }
                }

                if (newDocs > 0)
                {
                    await dbContext.SaveChangesAsync();
                    _newDocuments += newDocs;
                }

                drive.LastScanTime = DateTime.Now;
                drive.DocumentCount = await dbContext.JumpDocuments
                    .Where(d => d.SourceDrive == (drive.ParentDriveName ?? drive.DriveName))
                    .CountAsync();

                await dbContext.SaveChangesAsync();

                _drivesScanned++;
                logger.LogInformation("Completed scan of {DriveName}: {NewDocs} new documents", drive.DriveName, newDocs);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error scanning drive {DriveName}", drive.DriveName);
                _lastError = $"Error scanning {drive.DriveName}: {ex.Message}";
                // Continue with next drive
            }
        }

        // Refresh document count cache
        await documentCountService.RefreshCountAsync();

        logger.LogInformation("Background drive scan completed: {DrivesScanned}/{TotalDrives} drives, {NewDocuments} new documents",
            _drivesScanned, _totalDrives, _newDocuments);
    }
}

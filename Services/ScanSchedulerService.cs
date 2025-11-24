using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.IO;

namespace JumpChainSearch.Services;

/// <summary>
/// Background service that runs scheduled Google Drive scans based on configured intervals
/// </summary>
public class ScanSchedulerService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ScanSchedulerService> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(5); // Check every 5 minutes

    public ScanSchedulerService(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<ScanSchedulerService> logger)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Scan Scheduler Service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAndExecuteScheduledScan(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in scan scheduler");
            }

            // Wait before next check
            await Task.Delay(_checkInterval, stoppingToken);
        }

        _logger.LogInformation("Scan Scheduler Service stopped");
    }

    private async Task CheckAndExecuteScheduledScan(CancellationToken cancellationToken)
    {
        // Check if scheduling is enabled
        var enabled = _configuration.GetValue<bool>("ScanScheduling:Enabled", false);
        if (!enabled)
        {
            _logger.LogTrace("Scan scheduling is disabled");
            return;
        }

        // Get next scheduled scan time
        var nextScanStr = _configuration.GetValue<string>("ScanScheduling:NextScheduledScan");
        if (string.IsNullOrEmpty(nextScanStr))
        {
            _logger.LogWarning("Scan scheduling enabled but no next scan time configured");
            return;
        }

        if (!DateTime.TryParse(nextScanStr, out var nextScan))
        {
            _logger.LogWarning("Invalid next scan time format: {NextScanStr}", nextScanStr);
            return;
        }

        // Check if it's time to scan
        var now = DateTime.UtcNow;
        if (now < nextScan)
        {
            var timeUntilScan = nextScan - now;
            _logger.LogTrace("Next scan in {TimeUntilScan}", timeUntilScan);
            return;
        }

        _logger.LogInformation("Starting scheduled Google Drive scan");

        try
        {
            // Create a scope for scoped services
            using var scope = _serviceProvider.CreateScope();
            var driveService = scope.ServiceProvider.GetRequiredService<IGoogleDriveService>();
            var dbContext = scope.ServiceProvider.GetRequiredService<Data.JumpChainDbContext>();
            var documentCountService = scope.ServiceProvider.GetRequiredService<IDocumentCountService>();

            // Get all active drives
            var drives = await dbContext.DriveConfigurations
                .Where(d => d.IsActive)
                .ToListAsync(cancellationToken);

            if (drives.Count == 0)
            {
                _logger.LogWarning("No active drives configured for scheduled scan");
                return;
            }

            _logger.LogInformation("Scanning {DriveCount} active drives", drives.Count);

            var totalNewDocuments = 0;
            var totalUpdatedDocuments = 0;

            foreach (var drive in drives)
            {
                try
                {
                    _logger.LogInformation("Scanning drive: {DriveName}", drive.DriveName);
                    
                    var (documents, successfulMethod) = await driveService.ScanDriveUnifiedAsync(drive);
                    var documentsList = documents.ToList();
                    
                    // Update preferred auth method if needed
                    if (successfulMethod != "None" && drive.PreferredAuthMethod != successfulMethod)
                    {
                        drive.PreferredAuthMethod = successfulMethod;
                        await dbContext.SaveChangesAsync(cancellationToken);
                    }
                    
                    // Count new vs existing documents
                    var fileIds = documentsList.Select(d => d.GoogleDriveFileId).ToList();
                    var existingCount = await dbContext.JumpDocuments
                        .Where(d => fileIds.Contains(d.GoogleDriveFileId))
                        .CountAsync(cancellationToken);
                    
                    var newDocs = documentsList.Count - existingCount;
                    totalNewDocuments += newDocs;
                    totalUpdatedDocuments += existingCount;
                    
                    _logger.LogInformation(
                        "Drive {DriveName}: {NewDocs} new, {ExistingDocs} existing",
                        drive.DriveName,
                        newDocs,
                        existingCount);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error scanning drive {DriveName}", drive.DriveName);
                }
            }
            
            _logger.LogInformation(
                "Scheduled scan completed: {NewDocs} new documents, {UpdatedDocs} existing",
                totalNewDocuments,
                totalUpdatedDocuments);

            // Refresh document count cache
            await documentCountService.RefreshCountAsync();
            _logger.LogInformation("Document count cache refreshed");

            // Update last scan time and calculate next scan
            var intervalHours = _configuration.GetValue<int>("ScanScheduling:IntervalHours", 24);
            await UpdateScanSchedule(now, intervalHours);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing scheduled scan");
        }
    }

    private async Task UpdateScanSchedule(DateTime lastScanTime, int intervalHours)
    {
        try
        {
            var appsettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            
            if (!File.Exists(appsettingsPath))
            {
                _logger.LogWarning("appsettings.json not found at {Path}", appsettingsPath);
                return;
            }

            var json = await File.ReadAllTextAsync(appsettingsPath);
            var jsonDoc = System.Text.Json.JsonDocument.Parse(json);

            using var stream = new MemoryStream();
            using var writer = new System.Text.Json.Utf8JsonWriter(stream, new System.Text.Json.JsonWriterOptions { Indented = true });

            writer.WriteStartObject();
            foreach (var property in jsonDoc.RootElement.EnumerateObject())
            {
                if (property.Name == "ScanScheduling")
                {
                    writer.WriteStartObject("ScanScheduling");

                    // Preserve Enabled setting
                    var isEnabled = property.Value.TryGetProperty("Enabled", out var enabled) ? enabled.GetBoolean() : false;
                    writer.WriteBoolean("Enabled", isEnabled);
                    writer.WriteNumber("IntervalHours", intervalHours);

                    // Update scan times
                    writer.WriteString("LastScanTime", lastScanTime.ToString("o"));
                    
                    var nextScan = lastScanTime.AddHours(intervalHours);
                    writer.WriteString("NextScheduledScan", nextScan.ToString("o"));

                    writer.WriteEndObject();
                }
                else
                {
                    property.WriteTo(writer);
                }
            }
            writer.WriteEndObject();
            await writer.FlushAsync();

            await File.WriteAllBytesAsync(appsettingsPath, stream.ToArray());
            
            _logger.LogInformation(
                "Updated scan schedule: Last={LastScan}, Next={NextScan}",
                lastScanTime,
                lastScanTime.AddHours(intervalHours));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update scan schedule in appsettings.json");
        }
    }
}

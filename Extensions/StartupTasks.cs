using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using JumpChainSearch.Data;
using JumpChainSearch.Services;
using JumpChainSearch.Models;
using System.Text.Json;

namespace JumpChainSearch.Extensions;

public static class StartupTasks
{
    public static async Task RunAllAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<JumpChainDbContext>();
        // Skip database creation - database should already exist in production
        // context.Database.EnsureCreated();

        // Skip FTS5 initialization - FTS5 tables already exist in production database
        // Console.WriteLine("Initializing FTS5 full-text search...");
        // var fts5Setup = scope.ServiceProvider.GetRequiredService<Fts5SetupService>();
        // await fts5Setup.InitializeFts5Async();
        // Console.WriteLine("FTS5 initialization complete.");

        // Initialize drive configurations from .env if table is empty
        if (!context.DriveConfigurations.Any())
        {
            Console.WriteLine("Initializing drive configurations from .env...");
            var drivesConfig = Environment.GetEnvironmentVariable("JUMPCHAIN_DRIVES_CONFIG");
            if (!string.IsNullOrEmpty(drivesConfig))
            {
                try
                {
                    var drives = JsonSerializer.Deserialize<List<JumpChainDriveConfig>>(drivesConfig);
                    if (drives != null)
                    {
                        foreach (var drive in drives)
                        {
                            context.DriveConfigurations.Add(new DriveConfiguration
                            {
                                DriveId = drive.folderId,
                                DriveName = drive.name,
                                ResourceKey = drive.resourceKey,
                                ParentDriveName = drive.parentDriveName,
                                Description = $"JumpChain community drive",
                                IsActive = true,
                                LastScanTime = DateTime.MinValue,
                                DocumentCount = 0
                            });
                        }
                        context.SaveChanges();
                        Console.WriteLine($"Initialized {drives.Count} drive configurations.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Could not initialize drive configurations: {ex.Message}");
                }
            }
        }

        // Skip automatic tag generation for now to avoid startup errors
        Console.WriteLine("Skipping automatic tag generation on startup.");

        // Initialize document count on startup
        var documentCountService = scope.ServiceProvider.GetRequiredService<IDocumentCountService>();
        await documentCountService.RefreshCountAsync();
    }
}

using JumpChainSearch.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace JumpChainSearch.Services;

/// <summary>
/// Service to set up and manage SQLite FTS5 full-text search
/// </summary>
public class Fts5SetupService
{
    private readonly JumpChainDbContext _context;
    private readonly ILogger<Fts5SetupService> _logger;

    public Fts5SetupService(JumpChainDbContext context, ILogger<Fts5SetupService> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Initialize FTS5 table and triggers if they don't exist
    /// </summary>
    public async Task InitializeFts5Async()
    {
        try
        {
            // Check if FTS5 table already exists using ExecuteScalarAsync
            var connection = _context.Database.GetDbConnection();
            await connection.OpenAsync();
            
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='JumpDocuments_fts'";
            var result = await command.ExecuteScalarAsync();
            var tableExists = Convert.ToInt32(result) > 0;

            if (tableExists)
            {
                _logger.LogInformation("FTS5 table already exists, skipping setup");
                return;
            }

            _logger.LogInformation("Setting up FTS5 full-text search...");

            // Create FTS5 virtual table (contentless for space efficiency)
            await _context.Database.ExecuteSqlRawAsync(@"
                CREATE VIRTUAL TABLE JumpDocuments_fts USING fts5(
                    Name,
                    FolderPath,
                    Tags,
                    ExtractedText,
                    content=''
                );
            ");

            // Create triggers for auto-sync
            await CreateTriggersAsync();

            // Populate with existing data
            await PopulateFts5Async();

            _logger.LogInformation("FTS5 setup completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting up FTS5");
            throw;
        }
    }

    private async Task CreateTriggersAsync()
    {
        // Trigger: Insert on JumpDocuments
        await _context.Database.ExecuteSqlRawAsync(@"
            CREATE TRIGGER JumpDocuments_ai AFTER INSERT ON JumpDocuments BEGIN
                INSERT INTO JumpDocuments_fts(rowid, Name, FolderPath, Tags, ExtractedText)
                SELECT 
                    new.Id,
                    new.Name,
                    new.FolderPath,
                    COALESCE((SELECT GROUP_CONCAT(TagName, ' ') FROM DocumentTags WHERE JumpDocumentId = new.Id), ''),
                    COALESCE(new.ExtractedText, '');
            END;
        ");

        // Trigger: Update on JumpDocuments
        await _context.Database.ExecuteSqlRawAsync(@"
            CREATE TRIGGER JumpDocuments_au AFTER UPDATE ON JumpDocuments BEGIN
                INSERT INTO JumpDocuments_fts(JumpDocuments_fts, rowid, Name, FolderPath, Tags, ExtractedText)
                SELECT 
                    'delete',
                    old.Id,
                    old.Name,
                    old.FolderPath,
                    COALESCE((SELECT GROUP_CONCAT(TagName, ' ') FROM DocumentTags WHERE JumpDocumentId = old.Id), ''),
                    COALESCE(old.ExtractedText, '');
                
                INSERT INTO JumpDocuments_fts(rowid, Name, FolderPath, Tags, ExtractedText)
                SELECT 
                    new.Id,
                    new.Name,
                    new.FolderPath,
                    COALESCE((SELECT GROUP_CONCAT(TagName, ' ') FROM DocumentTags WHERE JumpDocumentId = new.Id), ''),
                    COALESCE(new.ExtractedText, '');
            END;
        ");

        // Trigger: Delete on JumpDocuments
        await _context.Database.ExecuteSqlRawAsync(@"
            CREATE TRIGGER JumpDocuments_ad AFTER DELETE ON JumpDocuments BEGIN
                INSERT INTO JumpDocuments_fts(JumpDocuments_fts, rowid, Name, FolderPath, Tags, ExtractedText)
                SELECT 
                    'delete',
                    old.Id,
                    old.Name,
                    old.FolderPath,
                    COALESCE((SELECT GROUP_CONCAT(TagName, ' ') FROM DocumentTags WHERE JumpDocumentId = old.Id), ''),
                    COALESCE(old.ExtractedText, '');
            END;
        ");

        // Trigger: Insert on DocumentTags
        await _context.Database.ExecuteSqlRawAsync(@"
            CREATE TRIGGER DocumentTags_ai AFTER INSERT ON DocumentTags BEGIN
                INSERT INTO JumpDocuments_fts(JumpDocuments_fts, rowid, Name, FolderPath, Tags, ExtractedText)
                SELECT 
                    'delete',
                    d.Id,
                    d.Name,
                    d.FolderPath,
                    COALESCE((SELECT GROUP_CONCAT(TagName, ' ') FROM DocumentTags WHERE JumpDocumentId = d.Id AND Id != new.Id), ''),
                    COALESCE(d.ExtractedText, '')
                FROM JumpDocuments d WHERE d.Id = new.JumpDocumentId;
                
                INSERT INTO JumpDocuments_fts(rowid, Name, FolderPath, Tags, ExtractedText)
                SELECT 
                    d.Id,
                    d.Name,
                    d.FolderPath,
                    COALESCE((SELECT GROUP_CONCAT(TagName, ' ') FROM DocumentTags WHERE JumpDocumentId = d.Id), ''),
                    COALESCE(d.ExtractedText, '')
                FROM JumpDocuments d WHERE d.Id = new.JumpDocumentId;
            END;
        ");

        // Trigger: Update on DocumentTags
        await _context.Database.ExecuteSqlRawAsync(@"
            CREATE TRIGGER DocumentTags_au AFTER UPDATE ON DocumentTags BEGIN
                INSERT INTO JumpDocuments_fts(JumpDocuments_fts, rowid, Name, FolderPath, Tags, ExtractedText)
                SELECT 
                    'delete',
                    d.Id,
                    d.Name,
                    d.FolderPath,
                    COALESCE((SELECT GROUP_CONCAT(TagName, ' ') FROM DocumentTags WHERE JumpDocumentId = d.Id), ''),
                    COALESCE(d.ExtractedText, '')
                FROM JumpDocuments d WHERE d.Id = old.JumpDocumentId;
                
                INSERT INTO JumpDocuments_fts(rowid, Name, FolderPath, Tags, ExtractedText)
                SELECT 
                    d.Id,
                    d.Name,
                    d.FolderPath,
                    COALESCE((SELECT GROUP_CONCAT(TagName, ' ') FROM DocumentTags WHERE JumpDocumentId = d.Id), ''),
                    COALESCE(d.ExtractedText, '')
                FROM JumpDocuments d WHERE d.Id = new.JumpDocumentId;
            END;
        ");

        // Trigger: Delete on DocumentTags
        await _context.Database.ExecuteSqlRawAsync(@"
            CREATE TRIGGER DocumentTags_ad AFTER DELETE ON DocumentTags BEGIN
                INSERT INTO JumpDocuments_fts(JumpDocuments_fts, rowid, Name, FolderPath, Tags, ExtractedText)
                SELECT 
                    'delete',
                    d.Id,
                    d.Name,
                    d.FolderPath,
                    COALESCE((SELECT GROUP_CONCAT(TagName, ' ') FROM DocumentTags WHERE JumpDocumentId = d.Id AND Id != old.Id), ''),
                    COALESCE(d.ExtractedText, '')
                FROM JumpDocuments d WHERE d.Id = old.JumpDocumentId;
                
                INSERT INTO JumpDocuments_fts(rowid, Name, FolderPath, Tags, ExtractedText)
                SELECT 
                    d.Id,
                    d.Name,
                    d.FolderPath,
                    COALESCE((SELECT GROUP_CONCAT(TagName, ' ') FROM DocumentTags WHERE JumpDocumentId = d.Id), ''),
                    COALESCE(d.ExtractedText, '')
                FROM JumpDocuments d WHERE d.Id = old.JumpDocumentId;
            END;
        ");
    }

    private async Task PopulateFts5Async()
    {
        _logger.LogInformation("Populating FTS5 table with existing documents...");
        
        await _context.Database.ExecuteSqlRawAsync(@"
            INSERT INTO JumpDocuments_fts(rowid, Name, FolderPath, Tags, ExtractedText)
            SELECT 
                d.Id,
                d.Name,
                d.FolderPath,
                COALESCE((SELECT GROUP_CONCAT(TagName, ' ') FROM DocumentTags WHERE JumpDocumentId = d.Id), ''),
                COALESCE(d.ExtractedText, '')
            FROM JumpDocuments d;
        ");

        // Get count using ExecuteScalarAsync
        var connection = _context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();
        
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM JumpDocuments_fts";
        var result = await command.ExecuteScalarAsync();
        var count = Convert.ToInt32(result);
        
        _logger.LogInformation("Populated FTS5 table with {Count} documents", count);
    }

    /// <summary>
    /// Rebuild FTS5 index (useful for maintenance)
    /// </summary>
    public async Task RebuildFts5Async()
    {
        _logger.LogInformation("Rebuilding FTS5 index...");
        await _context.Database.ExecuteSqlRawAsync("INSERT INTO JumpDocuments_fts(JumpDocuments_fts) VALUES('rebuild');");
        _logger.LogInformation("FTS5 index rebuilt");
    }

    /// <summary>
    /// Optimize FTS5 index (merge b-trees for better performance)
    /// </summary>
    public async Task OptimizeFts5Async()
    {
        _logger.LogInformation("Optimizing FTS5 index...");
        await _context.Database.ExecuteSqlRawAsync("INSERT INTO JumpDocuments_fts(JumpDocuments_fts) VALUES('optimize');");
        _logger.LogInformation("FTS5 index optimized");
    }
}

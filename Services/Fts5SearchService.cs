using System.Text;
using JumpChainSearch.Data;
using Microsoft.EntityFrameworkCore;

namespace JumpChainSearch.Services;

/// <summary>
/// Service for executing FTS5 full-text search queries
/// </summary>
public class Fts5SearchService
{
    private readonly JumpChainDbContext _context;

    public Fts5SearchService(JumpChainDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Convert user search terms into FTS5 query syntax
    /// </summary>
    public string BuildFts5Query(List<string> searchTerms, List<string> phrases, List<string> excludedTerms)
    {
        var queryParts = new List<string>();

        // Add regular search terms with AND logic
        foreach (var term in searchTerms)
        {
            // Use prefix matching for partial terms to allow 'zom' -> matches 'zombie'
            // but avoid applying to very short terms to reduce noise
            var escaped = EscapeFts5Term(term);
            if (!string.IsNullOrWhiteSpace(escaped) && escaped.Length >= 3)
            {
                queryParts.Add(escaped + "*");
            }
            else
            {
                queryParts.Add(escaped);
            }
        }

        // Add quoted phrases
        foreach (var phrase in phrases)
        {
            queryParts.Add($"\"{EscapeFts5Term(phrase)}\"");
        }

        // Combine terms with AND
        var positiveQuery = string.Join(" AND ", queryParts.Where(p => !string.IsNullOrEmpty(p)));

        // Add excluded terms with NOT
        // Apply prefix exclusion too when sensible
        var excludeParts = excludedTerms.Select(term =>
        {
            var e = EscapeFts5Term(term);
            return (!string.IsNullOrWhiteSpace(e) && e.Length >= 3) ? $"NOT {e}*" : $"NOT {e}";
        });
        var excludeQuery = string.Join(" ", excludeParts);

        // Combine positive and negative parts
        if (!string.IsNullOrEmpty(positiveQuery) && !string.IsNullOrEmpty(excludeQuery))
        {
            return $"({positiveQuery}) {excludeQuery}";
        }
        else if (!string.IsNullOrEmpty(positiveQuery))
        {
            return positiveQuery;
        }
        else if (!string.IsNullOrEmpty(excludeQuery))
        {
            return excludeQuery;
        }

        return "*"; // Match all if no terms
    }

    /// <summary>
    /// Escape special FTS5 characters in search terms
    /// </summary>
    private string EscapeFts5Term(string term)
    {
        if (string.IsNullOrWhiteSpace(term))
            return "";

        // FTS5 special characters that need escaping: " (double quote)
        // Quotes are already handled by caller when building phrases
        return term.Replace("\"", "\"\"");
    }

    /// <summary>
    /// Execute FTS5 search and return document IDs with scores
    /// </summary>
    public async Task<List<(int Id, double Score)>> SearchFts5Async(string fts5Query, int limit, int offset)
    {
        Console.WriteLine($"[FTS5] Executing search: query='{fts5Query}', limit={limit}, offset={offset}");
        
        // Use parameterized query to prevent SQL injection
        var sql = @"
            SELECT 
                rowid as Id,
                bm25(JumpDocuments_fts, 10.0, 5.0, 3.0, 1.0) as Score
            FROM JumpDocuments_fts
            WHERE JumpDocuments_fts MATCH {0}
            ORDER BY Score
            LIMIT {1} OFFSET {2}";

        var connection = _context.Database.GetDbConnection();
        var shouldClose = connection.State == System.Data.ConnectionState.Closed;
        
        if (shouldClose)
            await connection.OpenAsync();

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = string.Format(sql, "@p0", "@p1", "@p2");
            command.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter("@p0", fts5Query));
            command.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter("@p1", limit));
            command.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter("@p2", offset));

            var results = new List<(int Id, double Score)>();
            
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var id = reader.GetInt32(0);
                var score = reader.GetDouble(1);
                results.Add((id, score));
            }

            Console.WriteLine($"[FTS5] Found {results.Count} results");
            return results;
        }
        finally
        {
            if (shouldClose && connection.State == System.Data.ConnectionState.Open)
                await connection.CloseAsync();
        }
    }

    /// <summary>
    /// Get total count of documents matching FTS5 query
    /// </summary>
    public async Task<int> GetFts5CountAsync(string fts5Query)
    {
        Console.WriteLine($"[FTS5] Getting count for query: '{fts5Query}'");
        
        var sql = @"
            SELECT COUNT(*)
            FROM JumpDocuments_fts
            WHERE JumpDocuments_fts MATCH {0}";

        var connection = _context.Database.GetDbConnection();
        var shouldClose = connection.State == System.Data.ConnectionState.Closed;
        
        if (shouldClose)
            await connection.OpenAsync();

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = string.Format(sql, "@p0");
            command.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter("@p0", fts5Query));

            var count = await command.ExecuteScalarAsync();
            var result = Convert.ToInt32(count);
            
            Console.WriteLine($"[FTS5] Count result: {result}");
            return result;
        }
        finally
        {
            if (shouldClose && connection.State == System.Data.ConnectionState.Open)
                await connection.CloseAsync();
        }
    }
}

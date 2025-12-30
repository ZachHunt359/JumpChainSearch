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
    /// Execute FTS5 search and return document IDs with scores, including title boost for better relevance.
    /// Uses a fixed boost pool size to ensure consistent sorting across pagination.
    /// </summary>
    public async Task<List<(int Id, double Score)>> SearchFts5Async(string fts5Query, int limit, int offset, List<string>? searchTerms = null, List<string>? phrases = null)
    {
        Console.WriteLine($"[FTS5] Executing search: query='{fts5Query}', limit={limit}, offset={offset}");
        
        // Title boost configuration
        // We maintain a consistent boost pool to ensure sorting doesn't change across pagination
        const int BOOST_POOL_SIZE = 500; // Covers first ~10 pages (at 50 results/page)
        
        bool shouldApplyBoost = (searchTerms?.Any() == true || phrases?.Any() == true) && offset < BOOST_POOL_SIZE;
        
        int fetchLimit;
        int fetchOffset;
        
        if (shouldApplyBoost)
        {
            // Fetch entire boost pool from the beginning for consistent sorting
            // We'll apply pagination AFTER boosting to maintain consistent ranking across pages
            fetchLimit = BOOST_POOL_SIZE;
            fetchOffset = 0;
            Console.WriteLine($"[FTS5] Title boost active: fetching {fetchLimit} results for consistent sorting");
        }
        else
        {
            // Beyond boost pool or no search terms - use raw BM25 pagination
            fetchLimit = limit;
            fetchOffset = offset;
            if (offset >= BOOST_POOL_SIZE)
            {
                Console.WriteLine($"[FTS5] Beyond boost pool (offset {offset} >= {BOOST_POOL_SIZE}), using raw BM25");
            }
        }
        
        // Use parameterized query to prevent SQL injection
        // Fetch Name column along with scores for title boost calculation
        var sql = @"
            SELECT 
                rowid as Id,
                Name,
                bm25(JumpDocuments_fts, 10.0, 5.0, 3.0, 1.0) as BM25Score
            FROM JumpDocuments_fts
            WHERE JumpDocuments_fts MATCH {0}
            ORDER BY BM25Score
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
            command.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter("@p1", fetchLimit));
            command.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter("@p2", fetchOffset));

            var rawResults = new List<(int Id, string Name, double BM25Score)>();
            
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var id = reader.GetInt32(0);
                var name = reader.IsDBNull(1) ? "" : reader.GetString(1);
                var bm25Score = reader.GetDouble(2);
                rawResults.Add((id, name, bm25Score));
            }

            Console.WriteLine($"[FTS5] Found {rawResults.Count} raw results from database");
            
            if (shouldApplyBoost && rawResults.Any())
            {
                // Calculate title boost and combine with BM25 score for ALL results in pool
                var boostedResults = rawResults.Select(r => 
                {
                    var titleBoost = CalculateTitleBoost(r.Name, searchTerms ?? new List<string>(), phrases ?? new List<string>());
                    var finalScore = r.BM25Score + titleBoost;
                    
                    if (titleBoost < 0) // Only log actual boosts (negative is better)
                    {
                        Console.WriteLine($"[FTS5] Title boost for '{r.Name}': {titleBoost:F1} (BM25: {r.BM25Score:F2}, Final: {finalScore:F2})");
                    }
                    
                    return (r.Id, finalScore);
                })
                .OrderBy(r => r.finalScore) // Lower (more negative) is better - boosts push results up
                .ToList();
                
                // NOW apply pagination to the consistently-sorted boosted results
                var paginatedResults = boostedResults.Skip(offset).Take(limit).ToList();
                
                Console.WriteLine($"[FTS5] After title boost and pagination: returning {paginatedResults.Count} results (from pool of {boostedResults.Count})");
                return paginatedResults;
            }
            else
            {
                // No boost applied - return raw BM25 results (already paginated by SQL)
                var results = rawResults.Select(r => (r.Id, r.BM25Score)).ToList();
                Console.WriteLine($"[FTS5] Returning {results.Count} raw BM25 results (no boost)");
                return results;
            }
        }
        finally
        {
            if (shouldClose && connection.State == System.Data.ConnectionState.Open)
                await connection.CloseAsync();
        }
    }
    
    /// <summary>
    /// Calculate title relevance boost score based on how well search terms match the document title
    /// Returns a negative number to add to BM25 score (more negative = better ranking)
    /// </summary>
    private double CalculateTitleBoost(string title, List<string> searchTerms, List<string> phrases)
    {
        if (string.IsNullOrWhiteSpace(title) || (searchTerms.Count == 0 && phrases.Count == 0))
            return 0;
        
        var titleLower = title.ToLowerInvariant();
        double boost = 0;
        
        // Check for exact phrase matches in title (highest priority)
        foreach (var phrase in phrases)
        {
            if (!string.IsNullOrWhiteSpace(phrase))
            {
                var phraseLower = phrase.ToLowerInvariant();
                if (titleLower.Contains(phraseLower))
                {
                    // Exact phrase in title - massive boost
                    boost -= 1000;
                    Console.WriteLine($"[TITLE BOOST] Exact phrase '{phrase}' found in title '{title}': -1000");
                }
            }
        }
        
        // If we have search terms, analyze them
        if (searchTerms.Count > 0)
        {
            var termsLower = searchTerms.Select(t => t.ToLowerInvariant()).ToList();
            
            // Check if ALL search terms appear in title
            var allTermsPresent = termsLower.All(term => titleLower.Contains(term));
            if (allTermsPresent)
            {
                boost -= 100;
                Console.WriteLine($"[TITLE BOOST] All terms present in title '{title}': -100");
                
                // Additional boost if terms appear in the same order as searched
                if (AreTermsInOrder(titleLower, termsLower))
                {
                    boost -= 50;
                    Console.WriteLine($"[TITLE BOOST] Terms in correct order in title '{title}': -50");
                }
            }
            else
            {
                // Partial match: give credit for each term that appears
                var matchCount = termsLower.Count(term => titleLower.Contains(term));
                if (matchCount > 0)
                {
                    var partialBoost = matchCount * -10;
                    boost += partialBoost;
                    Console.WriteLine($"[TITLE BOOST] {matchCount}/{termsLower.Count} terms in title '{title}': {partialBoost}");
                }
            }
        }
        
        return boost;
    }
    
    /// <summary>
    /// Check if search terms appear in the title in the same order they were searched
    /// </summary>
    private bool AreTermsInOrder(string titleLower, List<string> termsLower)
    {
        var lastIndex = -1;
        foreach (var term in termsLower)
        {
            var index = titleLower.IndexOf(term, lastIndex + 1);
            if (index <= lastIndex)
            {
                return false;
            }
            lastIndex = index;
        }
        return true;
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

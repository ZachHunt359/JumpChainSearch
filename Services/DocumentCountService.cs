using JumpChainSearch.Data;
using Microsoft.EntityFrameworkCore;

namespace JumpChainSearch.Services;

/// <summary>
/// Manages the total document count with in-memory caching for high-performance scenarios.
/// 
/// USAGE:
/// - Call IncrementCountAsync() AFTER adding documents and calling SaveChangesAsync()
/// - Call DecrementCountAsync() AFTER removing documents and calling SaveChangesAsync()
/// - Call RefreshCountAsync() to force a database query (e.g., after batch operations)
/// 
/// WHERE TO INTEGRATE:
/// This service should be injected and called in:
/// 1. GoogleDriveService.cs - After adding documents at lines ~120, ~201, ~245
/// 2. Any endpoint that removes/deletes documents from the database
/// 3. Any batch import/merge operations
/// 
/// PERFORMANCE:
/// - Zero database queries per page request (uses cached singleton value)
/// - Thread-safe for hundreds/thousands of concurrent users
/// - Auto-refreshes if count drops below 5,000 (safety mechanism)
/// 
/// See Services/DOCUMENT_COUNT_SERVICE_USAGE.md for detailed integration examples.
/// </summary>
public interface IDocumentCountService
{
    Task<int> GetCountAsync();
    Task RefreshCountAsync();
    Task IncrementCountAsync();
    Task DecrementCountAsync();
}

public class DocumentCountService : IDocumentCountService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private int _cachedCount = 0;
    private DateTime _lastRefresh = DateTime.MinValue;
    private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

    public DocumentCountService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<int> GetCountAsync()
    {
        // If count is suspiciously low, refresh it
        if (_cachedCount < 5000)
        {
            await RefreshCountAsync();
        }

        return _cachedCount;
    }

    public async Task RefreshCountAsync()
    {
        await _lock.WaitAsync();
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<JumpChainDbContext>();
            
            _cachedCount = await context.JumpDocuments.CountAsync();
            _lastRefresh = DateTime.UtcNow;
            
            Console.WriteLine($"[DocumentCountService] Refreshed count: {_cachedCount} documents");
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task IncrementCountAsync()
    {
        await _lock.WaitAsync();
        try
        {
            _cachedCount++;
            Console.WriteLine($"[DocumentCountService] Incremented count to: {_cachedCount}");
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task DecrementCountAsync()
    {
        await _lock.WaitAsync();
        try
        {
            _cachedCount--;
            Console.WriteLine($"[DocumentCountService] Decremented count to: {_cachedCount}");
            
            // If count drops below threshold, refresh to ensure accuracy
            if (_cachedCount < 5000)
            {
                _lock.Release(); // Release before calling RefreshCountAsync which will acquire it again
                await RefreshCountAsync();
                return;
            }
        }
        finally
        {
            if (_lock.CurrentCount == 0)
            {
                _lock.Release();
            }
        }
    }
}

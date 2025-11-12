using Microsoft.Extensions.Caching.Memory;

namespace JumpChainSearch.Services;

/// <summary>
/// Service to invalidate search cache when documents are updated
/// </summary>
public class SearchCacheInvalidationService
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<SearchCacheInvalidationService> _logger;

    public SearchCacheInvalidationService(IMemoryCache cache, ILogger<SearchCacheInvalidationService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Invalidate all search caches (call after document updates, tag changes, etc.)
    /// </summary>
    public void InvalidateAllSearchCaches()
    {
        // Since IMemoryCache doesn't provide a way to get all keys,
        // we'll use a tag-based approach
        _logger.LogInformation("Search cache invalidation requested");
        
        // For now, we can't iterate cache entries in IMemoryCache
        // But since our cache duration is 5 minutes, stale data will expire quickly
        // For immediate invalidation, we'd need a custom cache implementation or Redis
        
        // Alternative: Use a cache version number approach
        IncrementCacheVersion();
    }

    private void IncrementCacheVersion()
    {
        var currentVersion = _cache.Get<int>("cache_version");
        _cache.Set("cache_version", currentVersion + 1, TimeSpan.FromDays(365));
        _logger.LogInformation($"Cache version incremented to {currentVersion + 1}");
    }

    public int GetCacheVersion()
    {
        return _cache.GetOrCreate("cache_version", entry =>
        {
            entry.SetAbsoluteExpiration(TimeSpan.FromDays(365));
            return 0;
        });
    }
}

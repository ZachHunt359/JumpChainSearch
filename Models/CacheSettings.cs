namespace JumpChainSearch.Models;

public class CacheSettings
{
    public int SearchCacheDurationMinutes { get; set; } = 5;
    public int TagCacheDurationMinutes { get; set; } = 10;
}
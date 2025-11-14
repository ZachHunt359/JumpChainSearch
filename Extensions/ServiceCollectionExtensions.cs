using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using JumpChainSearch.Data;
using JumpChainSearch.Services;

namespace JumpChainSearch.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddJumpChainServices(this IServiceCollection services, string? connectionString)
    {
        // Add Entity Framework
        services.AddDbContext<JumpChainDbContext>(options =>
            options.UseSqlite(connectionString ?? "Data Source=jumpchain.db"));

        // Add custom services
        services.AddScoped<IGoogleDriveService, GoogleDriveService>();
        services.AddScoped<ISearchService, SearchService>();
        services.AddScoped<IPurchasableParsingService, PurchasableParsingService>();
        services.AddScoped<GenreTagService>();
        services.AddScoped<SeriesTagService>();
        services.AddScoped<AdminAuthService>();
        services.AddSingleton<SearchCacheInvalidationService>();
        services.AddScoped<Fts5SetupService>();
        services.AddScoped<Fts5SearchService>();
        services.AddSingleton<IDocumentCountService, DocumentCountService>();
        return services;
    }
}

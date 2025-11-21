using Microsoft.JSInterop;
using System.Text.Json;

namespace JumpChainSearch.Services
{
    public class FavoritesService
    {
        private readonly IJSRuntime _jsRuntime;
        private List<FavoriteDocument> _favorites = new();

        public FavoritesService(IJSRuntime jsRuntime)
        {
            _jsRuntime = jsRuntime;
        }

        public async Task<List<FavoriteDocument>> GetFavoritesAsync()
        {
            try
            {
                var json = await _jsRuntime.InvokeAsync<string>("localStorage.getItem", "jumpchain-favorites");
                if (!string.IsNullOrEmpty(json))
                {
                    var favorites = JsonSerializer.Deserialize<List<FavoriteDocument>>(json);
                    if (favorites != null)
                    {
                        _favorites = favorites;
                        return _favorites;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($@"Error loading favorites: {ex.Message}");
            }
            return _favorites;
        }

        public async Task AddFavoriteAsync(string id, string name, string url)
        {
            if (!_favorites.Any(f => f.Id == id))
            {
                _favorites.Add(new FavoriteDocument { Id = id, Name = name, Url = url });
                await SaveFavoritesAsync();
            }
        }

        public async Task RemoveFavoriteAsync(string id)
        {
            _favorites.RemoveAll(f => f.Id == id);
            await SaveFavoritesAsync();
        }

        public bool IsFavorite(string id)
        {
            return _favorites.Any(f => f.Id == id);
        }

        private async Task SaveFavoritesAsync()
        {
            try
            {
                var json = JsonSerializer.Serialize(_favorites);
                await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "jumpchain-favorites", json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($@"Error saving favorites: {ex.Message}");
            }
        }

        public class FavoriteDocument
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Url { get; set; } = string.Empty;
        }
    }
}

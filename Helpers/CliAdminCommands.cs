using JumpChainSearch.Data;
using JumpChainSearch.Services;
using Microsoft.EntityFrameworkCore;

namespace JumpChainSearch.Helpers;

public static class CliAdminCommands
{
    public static async Task<int> Handle(string[] args)
    {
        if (args.Length > 0 && args[0] == "create-admin")
        {
            if (args.Length < 3)
            {
                Console.WriteLine("Usage: dotnet run -- create-admin <username> <password>");
                Console.WriteLine("Password must be at least 8 characters.");
                return 1;
            }

            var username = args[1];
            var password = args[2];

            // Build minimal services for CLI command
            var tempBuilder = WebApplication.CreateBuilder();
            tempBuilder.Services.AddDbContext<JumpChainDbContext>(options =>
                options.UseSqlite(tempBuilder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=jumpchain.db"));
            tempBuilder.Services.AddScoped<AdminAuthService>();
            var tempApp = tempBuilder.Build();

            using (var scope = tempApp.Services.CreateScope())
            {
                var authService = scope.ServiceProvider.GetRequiredService<AdminAuthService>();
                try
                {
                    var user = await authService.CreateAdminUserAsync(username, password);
                    Console.WriteLine($"✓ Admin user '{username}' created successfully!");
                    Console.WriteLine($"  User ID: {user.Id}");
                    Console.WriteLine($"  Created: {user.CreatedAt}");
                    Console.WriteLine("\nYou can now login at: http://localhost:5248/admin/");
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"✗ Error creating admin user: {ex.Message}");
                    return 1;
                }
            }
        }
        return -1; // Not a CLI command
    }
}

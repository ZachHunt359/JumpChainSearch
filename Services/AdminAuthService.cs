using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using JumpChainSearch.Data;
using JumpChainSearch.Models;

namespace JumpChainSearch.Services;

public class AdminAuthService
{
    private readonly JumpChainDbContext _context;
    private const int SaltSize = 32;
    private const int HashSize = 64;
    private const int Iterations = 100000;

    public AdminAuthService(JumpChainDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Creates a new admin user with hashed password
    /// </summary>
    public async Task<AdminUser> CreateAdminUserAsync(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("Username cannot be empty", nameof(username));
        
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Password cannot be empty", nameof(password));

        if (password.Length < 8)
            throw new ArgumentException("Password must be at least 8 characters", nameof(password));

        // Check if username already exists
        var existing = await _context.AdminUsers.FirstOrDefaultAsync(u => u.Username == username);
        if (existing != null)
            throw new InvalidOperationException("Username already exists");

        // Generate salt and hash password
        var salt = GenerateSalt();
        var hash = HashPassword(password, salt);

        var user = new AdminUser
        {
            Username = username,
            Salt = Convert.ToBase64String(salt),
            PasswordHash = Convert.ToBase64String(hash),
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        _context.AdminUsers.Add(user);
        await _context.SaveChangesAsync();

        return user;
    }

    /// <summary>
    /// Validates username and password, returns session token if successful
    /// </summary>
    public async Task<(bool success, string? sessionToken, AdminUser? user)> LoginAsync(
        string username, string password, string ipAddress, string userAgent)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return (false, null, null);

        var user = await _context.AdminUsers.FirstOrDefaultAsync(u => u.Username == username);
        if (user == null || !user.IsActive)
            return (false, null, null);

        // Verify password
        var salt = Convert.FromBase64String(user.Salt);
        var hash = HashPassword(password, salt);
        var storedHash = Convert.FromBase64String(user.PasswordHash);

        if (!hash.SequenceEqual(storedHash))
            return (false, null, null);

        // Update last login
        user.LastLoginAt = DateTime.UtcNow;

        // Create session
        var sessionToken = GenerateSessionToken();
        var session = new AdminSession
        {
            SessionToken = sessionToken,
            AdminUserId = user.Id,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(24),
            IpAddress = ipAddress,
            UserAgent = userAgent
        };

        _context.AdminSessions.Add(session);
        await _context.SaveChangesAsync();

        return (true, sessionToken, user);
    }

    /// <summary>
    /// Validates a session token and returns the associated user
    /// </summary>
    public async Task<(bool valid, AdminUser? user)> ValidateSessionAsync(string sessionToken)
    {
        if (string.IsNullOrWhiteSpace(sessionToken))
            return (false, null);

        var session = await _context.AdminSessions
            .Include(s => s.AdminUser)
            .FirstOrDefaultAsync(s => s.SessionToken == sessionToken);

        if (session == null)
            return (false, null);

        // Check if session is expired
        if (session.ExpiresAt < DateTime.UtcNow)
        {
            _context.AdminSessions.Remove(session);
            await _context.SaveChangesAsync();
            return (false, null);
        }

        // Check if user is still active
        if (!session.AdminUser.IsActive)
            return (false, null);

        return (true, session.AdminUser);
    }

    /// <summary>
    /// Logs out a user by deleting their session
    /// </summary>
    public async Task<bool> LogoutAsync(string sessionToken)
    {
        if (string.IsNullOrWhiteSpace(sessionToken))
            return false;

        var session = await _context.AdminSessions
            .FirstOrDefaultAsync(s => s.SessionToken == sessionToken);

        if (session == null)
            return false;

        _context.AdminSessions.Remove(session);
        await _context.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// Cleans up expired sessions
    /// </summary>
    public async Task<int> CleanupExpiredSessionsAsync()
    {
        var expiredSessions = await _context.AdminSessions
            .Where(s => s.ExpiresAt < DateTime.UtcNow)
            .ToListAsync();

        if (expiredSessions.Any())
        {
            _context.AdminSessions.RemoveRange(expiredSessions);
            await _context.SaveChangesAsync();
        }

        return expiredSessions.Count;
    }

    /// <summary>
    /// Changes a user's password
    /// </summary>
    public async Task<bool> ChangePasswordAsync(string username, string oldPassword, string newPassword)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
            return false;

        var user = await _context.AdminUsers.FirstOrDefaultAsync(u => u.Username == username);
        if (user == null)
            return false;

        // Verify old password
        var salt = Convert.FromBase64String(user.Salt);
        var hash = HashPassword(oldPassword, salt);
        var storedHash = Convert.FromBase64String(user.PasswordHash);

        if (!hash.SequenceEqual(storedHash))
            return false;

        // Generate new salt and hash
        var newSalt = GenerateSalt();
        var newHash = HashPassword(newPassword, newSalt);

        user.Salt = Convert.ToBase64String(newSalt);
        user.PasswordHash = Convert.ToBase64String(newHash);

        // Invalidate all existing sessions
        var sessions = await _context.AdminSessions
            .Where(s => s.AdminUserId == user.Id)
            .ToListAsync();
        _context.AdminSessions.RemoveRange(sessions);

        await _context.SaveChangesAsync();
        return true;
    }

    private static byte[] GenerateSalt()
    {
        var salt = new byte[SaltSize];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(salt);
        return salt;
    }

    private static byte[] HashPassword(string password, byte[] salt)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(
            password,
            salt,
            Iterations,
            HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(HashSize);
    }
    
    /// <summary>
    /// Verify a password against stored hash (public helper for endpoints)
    /// </summary>
    public bool VerifyPassword(string password, string storedHashBase64, string saltBase64)
    {
        var salt = Convert.FromBase64String(saltBase64);
        var hash = HashPassword(password, salt);
        var storedHash = Convert.FromBase64String(storedHashBase64);
        return hash.SequenceEqual(storedHash);
    }

    private static string GenerateSessionToken()
    {
        var tokenBytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(tokenBytes);
        return Convert.ToBase64String(tokenBytes);
    }
}

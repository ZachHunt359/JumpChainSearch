using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using JumpChainSearch.Services;
using System.ComponentModel.DataAnnotations;

namespace JumpChainSearch.Pages.Admin;

public class LoginModel : PageModel
{
    private readonly AdminAuthService _authService;
    private readonly ILogger<LoginModel> _logger;

    [BindProperty]
    [Required(ErrorMessage = "Username is required")]
    public string Username { get; set; } = string.Empty;

    [BindProperty]
    [Required(ErrorMessage = "Password is required")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    public string? ErrorMessage { get; set; }

    public LoginModel(AdminAuthService authService, ILogger<LoginModel> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    public void OnGet()
    {
        // Check if already logged in
        if (Request.Cookies.TryGetValue("admin_session", out var sessionToken))
        {
            var isValid = _authService.ValidateSessionAsync(sessionToken).Result;
            if (isValid.valid)
            {
                Response.Redirect("/admin/");
            }
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            ErrorMessage = "Please provide both username and password.";
            return Page();
        }

        try
        {
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var userAgent = Request.Headers["User-Agent"].ToString();

            var loginResult = await _authService.LoginAsync(Username, Password, ipAddress, userAgent);

            if (!loginResult.success || string.IsNullOrEmpty(loginResult.sessionToken))
            {
                ErrorMessage = "Invalid username or password.";
                _logger.LogWarning("Failed login attempt for username: {Username} from IP: {IP}", Username, ipAddress);
                return Page();
            }

            // Get session to find expiry time
            var (valid, user) = await _authService.ValidateSessionAsync(loginResult.sessionToken);
            var expiresAt = DateTime.UtcNow.AddHours(24); // Default 24 hours

            // Set session cookie
            Response.Cookies.Append("admin_session", loginResult.sessionToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = Request.IsHttps, // Only require HTTPS in production
                SameSite = SameSiteMode.Strict,
                Expires = expiresAt
            });

            _logger.LogInformation("Successful login for user: {Username} from IP: {IP}", Username, ipAddress);

            // Redirect to admin portal
            return Redirect("/admin/");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login for username: {Username}", Username);
            ErrorMessage = "An error occurred during login. Please try again.";
            return Page();
        }
    }
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using JumpChainSearch.Services;

namespace JumpChainSearch.Pages.Admin;

public class LogoutModel : PageModel
{
    private readonly AdminAuthService _authService;
    private readonly ILogger<LogoutModel> _logger;

    public LogoutModel(AdminAuthService authService, ILogger<LogoutModel> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        return await OnPostAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var sessionToken = Request.Cookies["admin_session"];
        
        if (!string.IsNullOrEmpty(sessionToken))
        {
            await _authService.LogoutAsync(sessionToken);
            _logger.LogInformation("User logged out with session token");
        }

        // Delete cookie
        Response.Cookies.Delete("admin_session");

        // Redirect to login page
        return Redirect("/Admin/Login");
    }
}
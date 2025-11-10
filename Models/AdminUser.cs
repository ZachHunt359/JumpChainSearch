using System.ComponentModel.DataAnnotations;

namespace JumpChainSearch.Models;

public class AdminUser
{
    [Key]
    public int Id { get; set; }
    
    [Required]
    [StringLength(50)]
    public string Username { get; set; } = string.Empty;
    
    [Required]
    public string PasswordHash { get; set; } = string.Empty;
    
    [Required]
    public string Salt { get; set; } = string.Empty;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime? LastLoginAt { get; set; }
    
    public bool IsActive { get; set; } = true;
}

public class AdminSession
{
    [Key]
    public int Id { get; set; }
    
    [Required]
    public string SessionToken { get; set; } = string.Empty;
    
    public int AdminUserId { get; set; }
    public virtual AdminUser AdminUser { get; set; } = null!;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime ExpiresAt { get; set; }
    
    public string IpAddress { get; set; } = string.Empty;
    
    public string UserAgent { get; set; } = string.Empty;
}

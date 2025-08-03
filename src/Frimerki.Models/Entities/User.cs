using System.ComponentModel.DataAnnotations;

namespace Frimerki.Models.Entities;

public class User {
    public int Id { get; set; }

    [Required]
    [MaxLength(255)]
    public string Username { get; set; } = "";

    public int DomainId { get; set; }

    [Required]
    public string PasswordHash { get; set; } = "";

    [Required]
    public string Salt { get; set; } = "";

    [MaxLength(255)]
    public string FullName { get; set; }

    [Required]
    [MaxLength(50)]
    public string Role { get; set; } = "User";

    public bool CanReceive { get; set; } = true;

    public bool CanLogin { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastLogin { get; set; }

    // Account lockout properties
    public int FailedLoginAttempts { get; set; }
    public DateTime? LockoutEnd { get; set; }
    public DateTime? LastFailedLogin { get; set; }

    // Navigation properties
    public DomainSettings Domain { get; set; }
    public ICollection<Folder> Folders { get; set; } = [];
    public ICollection<UserMessage> UserMessages { get; set; } = [];
    public ICollection<MessageFlag> MessageFlags { get; set; } = [];
}

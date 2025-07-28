using System.ComponentModel.DataAnnotations;

namespace Frimerki.Models.Entities;

public class Folder
{
    public int Id { get; set; }
    
    public int UserId { get; set; }
    
    [Required]
    [MaxLength(255)]
    public string Name { get; set; } = string.Empty;
    
    [MaxLength(1)]
    public string Delimiter { get; set; } = "/";
    
    [MaxLength(50)]
    public string? SystemFolderType { get; set; }
    
    public string? Attributes { get; set; }
    
    public int UidNext { get; set; } = 1;
    
    public int UidValidity { get; set; } = 1;
    
    public int Exists { get; set; } = 0;
    
    public int Recent { get; set; } = 0;
    
    public int Unseen { get; set; } = 0;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    // Navigation properties
    public User User { get; set; } = null!;
    public ICollection<UserMessage> UserMessages { get; set; } = new List<UserMessage>();
}

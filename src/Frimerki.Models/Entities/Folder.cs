using System.ComponentModel.DataAnnotations;

namespace Frimerki.Models.Entities;

public class Folder {
    public int Id { get; set; }

    public int UserId { get; set; }

    [Required]
    [MaxLength(255)]
    public string Name { get; set; } = "";

    [MaxLength(1)]
    public string Delimiter { get; set; } = "/";

    [MaxLength(50)]
    public string SystemFolderType { get; set; }

    public string Attributes { get; set; }

    public int UidNext { get; set; } = 1;

    public int UidValidity { get; set; } = 1;

    public int Exists { get; set; }

    public int Recent { get; set; }

    public int Unseen { get; set; }

    public bool Subscribed { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public User User { get; set; }
    public ICollection<UserMessage> UserMessages { get; set; } = [];
}

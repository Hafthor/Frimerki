using System.ComponentModel.DataAnnotations;

namespace Frimerki.Models.Entities;

public record Folder {
    public int Id { get; init; }

    public int UserId { get; init; }

    [Required]
    [MaxLength(255)]
    public string Name { get; set; } = "";

    [MaxLength(50)]
    public string SystemFolderType { get; init; }

    public string Attributes { get; init; }

    public int UidNext { get; set; } = 1;

    public int UidValidity { get; init; } = 1;

    public int Exists { get; set; }

    public int Recent { get; set; }

    public int Unseen { get; set; }

    public bool Subscribed { get; set; } = true;

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    // Navigation properties
    public User User { get; init; }
    public ICollection<UserMessage> UserMessages { get; init; } = [];
}

using System.ComponentModel.DataAnnotations;

namespace Frimerki.Models.Entities;

public record MessageFlag {
    public int Id { get; init; }

    public int MessageId { get; init; }

    public int UserId { get; init; }

    [Required]
    [MaxLength(50)]
    public string FlagName { get; init; } = "";

    public bool IsSet { get; set; } = true;

    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Message Message { get; init; }
    public User User { get; init; }
}

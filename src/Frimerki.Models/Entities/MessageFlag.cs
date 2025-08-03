using System.ComponentModel.DataAnnotations;

namespace Frimerki.Models.Entities;

public class MessageFlag {
    public int Id { get; set; }

    public int MessageId { get; set; }

    public int UserId { get; set; }

    [Required]
    [MaxLength(50)]
    public string FlagName { get; set; } = "";

    public bool IsSet { get; set; } = true;

    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Message Message { get; set; }
    public User User { get; set; }
}

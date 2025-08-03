using System.ComponentModel.DataAnnotations;

namespace Frimerki.Models.Entities;

public class DomainSettings {
    public int Id { get; set; }

    [Required]
    [MaxLength(255)]
    public string Name { get; set; } = "";

    public int? CatchAllUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public User CatchAllUser { get; set; }
    public ICollection<User> Users { get; set; } = [];
    public ICollection<DkimKey> DkimKeys { get; set; } = [];
    public ICollection<UidValiditySequence> UidValiditySequences { get; set; } = [];
}

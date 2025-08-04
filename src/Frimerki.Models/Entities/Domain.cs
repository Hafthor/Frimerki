using System.ComponentModel.DataAnnotations;

namespace Frimerki.Models.Entities;

public record DomainSettings {
    public int Id { get; init; }

    [Required]
    [MaxLength(255)]
    public string Name { get; init; } = "";

    public int? CatchAllUserId { get; set; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    // Navigation properties
    public User CatchAllUser { get; init; }
    public ICollection<User> Users { get; init; } = [];
    public ICollection<DkimKey> DkimKeys { get; init; } = [];
    public ICollection<UidValiditySequence> UidValiditySequences { get; init; } = [];
}

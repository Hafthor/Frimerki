using System.ComponentModel.DataAnnotations;

namespace Frimerki.Models.Entities;

public class Domain {
    public int Id { get; set; }

    [Required]
    [MaxLength(255)]
    public string Name { get; set; } = "";

    public bool IsActive { get; set; } = true;

    public int? CatchAllUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public User? CatchAllUser { get; set; }
    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<DkimKey> DkimKeys { get; set; } = new List<DkimKey>();
    public ICollection<UidValiditySequence> UidValiditySequences { get; set; } = new List<UidValiditySequence>();
}

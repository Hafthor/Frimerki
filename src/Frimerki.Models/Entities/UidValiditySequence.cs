namespace Frimerki.Models.Entities;

public class UidValiditySequence {
    public int Id { get; set; }

    public int DomainId { get; set; }

    public int Value { get; set; } = 1;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public DomainSettings Domain { get; set; }
    public ICollection<Message> Messages { get; set; } = [];
}

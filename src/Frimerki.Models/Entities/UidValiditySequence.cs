namespace Frimerki.Models.Entities;

public record UidValiditySequence {
    public int Id { get; init; }

    public int DomainId { get; init; }

    public int Value { get; init; } = 1;

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    // Navigation properties
    public DomainSettings Domain { get; init; }
    public ICollection<Message> Messages { get; init; } = [];
}

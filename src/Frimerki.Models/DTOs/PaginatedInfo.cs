namespace Frimerki.Models.DTOs;

public record PaginatedInfo<T> {
    public List<T> Items { get; init; } = [];
    public int Skip { get; init; }
    public int Take { get; init; }
    public int? TotalCount { get; init; }
    public string NextUrl { get; init; }
    public Dictionary<string, object> AppliedFilters { get; init; }
}

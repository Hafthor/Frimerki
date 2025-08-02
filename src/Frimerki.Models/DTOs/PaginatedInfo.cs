namespace Frimerki.Models.DTOs;

public class PaginatedInfo<T> {
    public List<T> Items { get; set; } = [];
    public int Skip { get; set; }
    public int Take { get; set; }
    public int TotalCount { get; set; }
    public string NextUrl { get; set; }
    public Dictionary<string, object> AppliedFilters { get; set; }
}

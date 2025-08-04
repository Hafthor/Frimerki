using System.ComponentModel.DataAnnotations;

namespace Frimerki.Models.DTOs;

public record FolderRequest {
    [Required, StringLength(255)]
    public string Name { get; init; } = "";
    public string Attributes { get; set; }
    public bool Subscribed { get; init; } = true;
}

public class FolderUpdateRequest {
    [StringLength(255)]
    public string Name { get; init; }
    public bool? Subscribed { get; init; }
}

public class FolderResponse {
    public string Name { get; init; } = "";
    public string SystemFolderType { get; init; }
    public string Attributes { get; init; }
    public int UidNext { get; init; }
    public int UidValidity { get; init; }
    public int Exists { get; init; }
    public int Recent { get; init; }
    public int Unseen { get; init; }
    public bool Subscribed { get; init; }
    public DateTime CreatedAt { get; init; }
}

public class FolderListResponse {
    public string Name { get; init; } = "";
    public string SystemFolderType { get; init; }
    public string Attributes { get; set; }
    public bool Subscribed { get; init; }
    public int MessageCount { get; init; }
    public int UnseenCount { get; init; }
}

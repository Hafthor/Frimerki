using System.ComponentModel.DataAnnotations;

namespace Frimerki.Models.DTOs.Folder;

public class FolderRequest {
    [Required, StringLength(255)]
    public string Name { get; set; } = "";

    [StringLength(1)]
    public string Delimiter { get; set; } = "/";

    public string Attributes { get; set; }
    public bool Subscribed { get; set; } = true;
}

public class FolderUpdateRequest {
    [StringLength(255)]
    public string? Name { get; set; }

    [StringLength(1)]
    public string? Delimiter { get; set; }

    public string? Attributes { get; set; }
    public bool? Subscribed { get; set; }
}

public class FolderResponse {
    public string Name { get; set; } = "";
    public string Delimiter { get; set; } = "/";
    public string SystemFolderType { get; set; }
    public string Attributes { get; set; }
    public int UidNext { get; set; }
    public int UidValidity { get; set; }
    public int Exists { get; set; }
    public int Recent { get; set; }
    public int Unseen { get; set; }
    public bool Subscribed { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class FolderListResponse {
    public string Name { get; set; } = "";
    public string SystemFolderType { get; set; }
    public string Attributes { get; set; }
    public bool Subscribed { get; set; }
    public int MessageCount { get; set; }
    public int UnseenCount { get; set; }
}

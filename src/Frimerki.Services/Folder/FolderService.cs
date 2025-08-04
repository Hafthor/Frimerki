using Frimerki.Data;
using Frimerki.Models.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Frimerki.Services.Folder;

public class FolderService(EmailDbContext context, ILogger<FolderService> logger) : IFolderService {
    public async Task<List<FolderListResponse>> GetFoldersAsync(int userId) {
        logger.LogInformation("Getting folders for user {UserId}", userId);

        return await context.Folders
            .Where(f => f.UserId == userId)
            .Select(f => new FolderListResponse {
                Name = f.Name,
                SystemFolderType = f.SystemFolderType,
                Attributes = f.Attributes,
                Subscribed = f.Subscribed,
                MessageCount = f.Exists,
                UnseenCount = f.Unseen
            })
            .OrderBy(f => f.SystemFolderType == null ? 1 : 0) // System folders first
            .ThenBy(f => f.Name)
            .ToListAsync();
    }

    public async Task<FolderResponse> GetFolderAsync(int userId, string folderName) {
        logger.LogInformation("Getting folder {FolderName} for user {UserId}", folderName, userId);

        // URL decode the folder name to handle encoded paths like INBOX%2FWork
        var decodedFolderName = Uri.UnescapeDataString(folderName);

        return await context.Folders
            .Where(f => f.UserId == userId && f.Name == decodedFolderName)
            .Select(f => new FolderResponse {
                Name = f.Name,
                SystemFolderType = f.SystemFolderType,
                Attributes = f.Attributes,
                UidNext = f.UidNext,
                UidValidity = f.UidValidity,
                Exists = f.Exists,
                Recent = f.Recent,
                Unseen = f.Unseen,
                Subscribed = f.Subscribed,
                CreatedAt = f.CreatedAt
            })
            .FirstOrDefaultAsync();
    }

    public async Task<FolderResponse> CreateFolderAsync(int userId, FolderRequest request) {
        logger.LogInformation("Creating folder {FolderName} for user {UserId}", request.Name, userId);

        // Validate folder name doesn't already exist
        if (await context.Folders.AnyAsync(f => f.UserId == userId && f.Name == request.Name)) {
            throw new InvalidOperationException($"Folder '{request.Name}' already exists");
        }

        // Validate parent folder exists for hierarchical folders
        if (request.Name.Contains('/')) {
            var parentPath = request.Name[..request.Name.LastIndexOf('/')];
            if (!await context.Folders.AnyAsync(f => f.UserId == userId && f.Name == parentPath)) {
                throw new InvalidOperationException($"Parent folder '{parentPath}' does not exist");
            }
        }

        var folder = new Models.Entities.Folder {
            UserId = userId,
            Name = request.Name,
            Attributes = request.Attributes,
            Subscribed = request.Subscribed,
            UidNext = 1,
            UidValidity = GenerateUidValidity(),
            Exists = 0,
            Recent = 0,
            Unseen = 0
        };

        context.Folders.Add(folder);
        await context.SaveChangesAsync();

        logger.LogInformation("Created folder {FolderName} with ID {FolderId} for user {UserId}",
            folder.Name, folder.Id, userId);

        return new FolderResponse {
            Name = folder.Name,
            SystemFolderType = folder.SystemFolderType,
            Attributes = folder.Attributes,
            UidNext = folder.UidNext,
            UidValidity = folder.UidValidity,
            Exists = folder.Exists,
            Recent = folder.Recent,
            Unseen = folder.Unseen,
            Subscribed = folder.Subscribed,
            CreatedAt = folder.CreatedAt
        };
    }

    public async Task<FolderResponse> UpdateFolderAsync(int userId, string folderName, FolderUpdateRequest request) {
        logger.LogInformation("Updating folder {FolderName} for user {UserId}", folderName, userId);

        var folder = await context.Folders
            .FirstOrDefaultAsync(f => f.UserId == userId && f.Name == folderName);

        if (folder == null) {
            return null;
        }

        // Prevent renaming system folders
        if (folder.SystemFolderType != null && !string.IsNullOrEmpty(request.Name) && request.Name != folder.Name) {
            throw new InvalidOperationException("Cannot rename system folders");
        }

        // Handle folder rename
        if (!string.IsNullOrEmpty(request.Name) && request.Name != folder.Name) {
            // Check if new name already exists
            if (await context.Folders.AnyAsync(f => f.UserId == userId && f.Name == request.Name)) {
                throw new InvalidOperationException($"Folder '{request.Name}' already exists");
            }

            // Update child folder names if this is a parent folder
            var childFolders = await context.Folders
                .Where(f => f.UserId == userId && f.Name.StartsWith(folder.Name + '/'))
                .ToListAsync();

            foreach (var child in childFolders) {
                child.Name = request.Name + child.Name[folder.Name.Length..];
            }

            folder.Name = request.Name;
        }

        // Update other properties
        if (request.Subscribed.HasValue) {
            folder.Subscribed = request.Subscribed.Value;
        }

        await context.SaveChangesAsync();

        logger.LogInformation("Updated folder {FolderName} for user {UserId}", folder.Name, userId);

        return new FolderResponse {
            Name = folder.Name,
            SystemFolderType = folder.SystemFolderType,
            Attributes = folder.Attributes,
            UidNext = folder.UidNext,
            UidValidity = folder.UidValidity,
            Exists = folder.Exists,
            Recent = folder.Recent,
            Unseen = folder.Unseen,
            Subscribed = folder.Subscribed,
            CreatedAt = folder.CreatedAt
        };
    }

    public async Task<bool> DeleteFolderAsync(int userId, string folderName) {
        logger.LogInformation("Deleting folder {FolderName} for user {UserId}", folderName, userId);

        var folder = await context.Folders
            .FirstOrDefaultAsync(f => f.UserId == userId && f.Name == folderName);

        if (folder == null) {
            return false;
        }

        // Prevent deletion of system folders
        if (folder.SystemFolderType != null) {
            throw new InvalidOperationException("Cannot delete system folders");
        }

        // Check if folder has messages
        var hasMessages = await context.UserMessages
            .AnyAsync(um => um.FolderId == folder.Id);

        if (hasMessages) {
            throw new InvalidOperationException("Cannot delete folder with messages. Move or delete messages first.");
        }

        // Delete child folders recursively
        var childFolders = await context.Folders
            .Where(f => f.UserId == userId && f.Name.StartsWith(folder.Name + '/'))
            .ToListAsync();

        foreach (var child in childFolders) {
            var childHasMessages = await context.UserMessages
                .AnyAsync(um => um.FolderId == child.Id);

            if (childHasMessages) {
                throw new InvalidOperationException($"Cannot delete folder with messages in subfolder '{child.Name}'. Move or delete messages first.");
            }
        }

        context.Folders.RemoveRange(childFolders);
        context.Folders.Remove(folder);
        await context.SaveChangesAsync();

        logger.LogInformation("Deleted folder {FolderName} and {ChildCount} child folders for user {UserId}",
            folderName, childFolders.Count, userId);

        return true;
    }

    private static int GenerateUidValidity() =>
        (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() & 0x7FFFFFFF);
}

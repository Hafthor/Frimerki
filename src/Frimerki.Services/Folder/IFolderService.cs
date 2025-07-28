using Frimerki.Models.DTOs.Folder;

namespace Frimerki.Services.Folder;

public interface IFolderService {
    Task<List<FolderListResponse>> GetFoldersAsync(int userId);
    Task<FolderResponse?> GetFolderAsync(int userId, string folderName);
    Task<FolderResponse> CreateFolderAsync(int userId, FolderRequest request);
    Task<FolderResponse?> UpdateFolderAsync(int userId, string folderName, FolderUpdateRequest request);
    Task<bool> DeleteFolderAsync(int userId, string folderName);
}

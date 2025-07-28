using Frimerki.Models.DTOs;

namespace Frimerki.Services.User;

public interface IUserService {
    Task<UserListResponse> GetUsersAsync(int page = 1, int pageSize = 50, string? domainFilter = null);
    Task<UserResponse?> GetUserByEmailAsync(string email);
    Task<UserResponse> CreateUserAsync(CreateUserRequest request);
    Task<UserResponse?> UpdateUserAsync(string email, UserUpdateRequest request);
    Task<bool> UpdateUserPasswordAsync(string email, UserPasswordUpdateRequest request);
    Task<bool> DeleteUserAsync(string email);
    Task<UserStatsResponse> GetUserStatsAsync(string email);
    Task<bool> UserExistsAsync(string email);
    Task<UserResponse?> AuthenticateUserAsync(string email, string password);
    Task<bool> ValidateEmailFormatAsync(string email);
    Task<bool> ValidateUsernameAsync(string username, string domainName);
}

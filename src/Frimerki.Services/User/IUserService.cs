using Frimerki.Models.DTOs;

namespace Frimerki.Services.User;

public interface IUserService {
    Task<PaginatedInfo<UserResponse>> GetUsersAsync(int skip = 1, int take = 50, string domainFilter = null);
    Task<UserResponse> GetUserByEmailAsync(string email);
    Task<UserResponse> CreateUserAsync(CreateUserRequest request);
    Task<UserResponse> UpdateUserAsync(string email, UserUpdateRequest request);
    Task<bool> UpdateUserPasswordAsync(string email, UserPasswordUpdateRequest request);
    Task<bool> DeleteUserAsync(string email);
    Task<UserStatsResponse> GetUserStatsAsync(string email);
    Task<bool> UserExistsAsync(string email);
    Task<UserResponse> AuthenticateUserAsync(string email, string password);
    Task<Frimerki.Models.Entities.User> AuthenticateUserEntityAsync(string email, string password);
    Task<Frimerki.Models.Entities.User> GetUserEntityByEmailAsync(string email);
    Task<bool> ValidateEmailFormatAsync(string email);
    Task<(bool IsLocked, DateTime? LockoutEnd)> GetAccountLockoutStatusAsync(string email);
}

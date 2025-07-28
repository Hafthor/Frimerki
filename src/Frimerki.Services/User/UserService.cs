using Frimerki.Data;
using Frimerki.Models.DTOs;
using Frimerki.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Frimerki.Services.User;

public class UserService : IUserService {
    private readonly EmailDbContext _context;
    private readonly ILogger<UserService> _logger;

    public UserService(EmailDbContext context, ILogger<UserService> logger) {
        _context = context;
        _logger = logger;
    }

    public async Task<UserListResponse> GetUsersAsync(int page = 1, int pageSize = 50, string? domainFilter = null) {
        _logger.LogInformation("Getting users list - Page: {Page}, PageSize: {PageSize}, Domain: {Domain}",
            page, pageSize, domainFilter ?? "All");

        var query = _context.Users
            .Include(u => u.Domain)
            .AsQueryable();

        if (!string.IsNullOrEmpty(domainFilter)) {
            query = query.Where(u => u.Domain.Name == domainFilter);
        }

        var totalCount = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        var users = await query
            .OrderBy(u => u.Domain.Name)
            .ThenBy(u => u.Username)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var userResponses = new List<UserResponse>();

        foreach (var user in users) {
            var stats = await GetUserStatsInternalAsync(user.Id);
            userResponses.Add(MapToUserResponse(user, stats));
        }

        return new UserListResponse {
            Users = userResponses,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = totalPages
        };
    }

    public async Task<UserResponse?> GetUserByEmailAsync(string email) {
        _logger.LogInformation("Getting user by email: {Email}", email);

        var user = await _context.Users
            .Include(u => u.Domain)
            .FirstOrDefaultAsync(u => u.Username + "@" + u.Domain.Name == email);

        if (user == null) {
            return null;
        }

        var stats = await GetUserStatsInternalAsync(user.Id);
        return MapToUserResponse(user, stats);
    }

    public async Task<UserResponse> CreateUserAsync(CreateUserRequest request) {
        _logger.LogInformation("Creating user: {Username}@{Domain}", request.Username, request.DomainName);

        // Validate domain exists
        var domain = await _context.Domains
            .FirstOrDefaultAsync(d => d.Name == request.DomainName);

        if (domain == null) {
            throw new ArgumentException($"Domain '{request.DomainName}' not found");
        }

        // Check if user already exists
        var existingUser = await _context.Users
            .AnyAsync(u => u.Username == request.Username && u.DomainId == domain.Id);

        if (existingUser) {
            throw new ArgumentException($"User '{request.Username}@{request.DomainName}' already exists");
        }

        // Generate salt and hash password
        var salt = GenerateSalt();
        var passwordHash = HashPassword(request.Password, salt);

        var user = new Frimerki.Models.Entities.User {
            Username = request.Username,
            DomainId = domain.Id,
            PasswordHash = passwordHash,
            Salt = salt,
            FullName = request.FullName,
            Role = request.Role,
            CanReceive = request.CanReceive,
            CanLogin = request.CanLogin,
            CreatedAt = DateTime.UtcNow
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        // Create default folders for the user
        await CreateDefaultFoldersAsync(user.Id);

        // Reload user with domain information
        var createdUser = await _context.Users
            .Include(u => u.Domain)
            .FirstAsync(u => u.Id == user.Id);

        var stats = new UserStatsResponse {
            MessageCount = 0,
            StorageUsed = 0,
            StorageUsedFormatted = "0 B",
            FolderCount = 6, // Default folders created
            LastActivity = null
        };

        _logger.LogInformation("User created successfully: {Email}", $"{request.Username}@{request.DomainName}");

        return MapToUserResponse(createdUser, stats);
    }

    public async Task<UserResponse?> UpdateUserAsync(string email, UserUpdateRequest request) {
        _logger.LogInformation("Updating user: {Email}", email);

        var user = await _context.Users
            .Include(u => u.Domain)
            .FirstOrDefaultAsync(u => u.Username + "@" + u.Domain.Name == email);

        if (user == null) {
            return null;
        }

        // Update fields if provided
        if (request.FullName != null) {
            user.FullName = request.FullName;
        }

        if (request.Role != null) {
            user.Role = request.Role;
        }

        if (request.CanReceive.HasValue) {
            user.CanReceive = request.CanReceive.Value;
        }

        if (request.CanLogin.HasValue) {
            user.CanLogin = request.CanLogin.Value;
        }

        await _context.SaveChangesAsync();

        var stats = await GetUserStatsInternalAsync(user.Id);
        return MapToUserResponse(user, stats);
    }

    public async Task<bool> UpdateUserPasswordAsync(string email, UserPasswordUpdateRequest request) {
        _logger.LogInformation("Updating password for user: {Email}", email);

        var user = await _context.Users
            .Include(u => u.Domain)
            .FirstOrDefaultAsync(u => u.Username + "@" + u.Domain.Name == email);

        if (user == null) {
            return false;
        }

        // Verify current password
        if (!VerifyPassword(request.CurrentPassword, user.PasswordHash, user.Salt)) {
            throw new UnauthorizedAccessException("Current password is incorrect");
        }

        // Generate new salt and hash new password
        var newSalt = GenerateSalt();
        var newPasswordHash = HashPassword(request.NewPassword, newSalt);

        user.PasswordHash = newPasswordHash;
        user.Salt = newSalt;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Password updated successfully for user: {Email}", email);
        return true;
    }

    public async Task<bool> DeleteUserAsync(string email) {
        _logger.LogInformation("Deleting user: {Email}", email);

        var user = await _context.Users
            .Include(u => u.Domain)
            .FirstOrDefaultAsync(u => u.Username + "@" + u.Domain.Name == email);

        if (user == null) {
            return false;
        }

        // TODO: Handle cascade deletes for user messages, folders, etc.
        // For now, we'll just delete the user record
        _context.Users.Remove(user);
        await _context.SaveChangesAsync();

        _logger.LogInformation("User deleted successfully: {Email}", email);
        return true;
    }

    public async Task<UserStatsResponse> GetUserStatsAsync(string email) {
        _logger.LogInformation("Getting stats for user: {Email}", email);

        var user = await _context.Users
            .Include(u => u.Domain)
            .FirstOrDefaultAsync(u => u.Username + "@" + u.Domain.Name == email);

        if (user == null) {
            throw new ArgumentException($"User '{email}' not found");
        }

        return await GetUserStatsInternalAsync(user.Id);
    }

    public async Task<bool> UserExistsAsync(string email) {
        return await _context.Users
            .Include(u => u.Domain)
            .AnyAsync(u => u.Username + "@" + u.Domain.Name == email);
    }

    public async Task<UserResponse?> AuthenticateUserAsync(string email, string password) {
        _logger.LogInformation("Authenticating user: {Email}", email);

        var user = await _context.Users
            .Include(u => u.Domain)
            .FirstOrDefaultAsync(u => u.Username + "@" + u.Domain.Name == email);

        if (user == null || !user.CanLogin) {
            return null;
        }

        if (!VerifyPassword(password, user.PasswordHash, user.Salt)) {
            return null;
        }

        // Update last login
        user.LastLogin = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var stats = await GetUserStatsInternalAsync(user.Id);
        return MapToUserResponse(user, stats);
    }

    public Task<bool> ValidateEmailFormatAsync(string email) {
        var emailRegex = new Regex(@"^[a-zA-Z0-9._-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$");
        return Task.FromResult(emailRegex.IsMatch(email));
    }

    public async Task<bool> ValidateUsernameAsync(string username, string domainName) {
        // Check username format
        var usernameRegex = new Regex(@"^[a-zA-Z0-9._-]+$");
        if (!usernameRegex.IsMatch(username)) {
            return false;
        }

        // Check if domain exists
        var domainExists = await _context.Domains
            .AnyAsync(d => d.Name == domainName);

        if (!domainExists) {
            return false;
        }

        // Check if username is available in domain
        var userExists = await _context.Users
            .Include(u => u.Domain)
            .AnyAsync(u => u.Username == username && u.Domain.Name == domainName);

        return !userExists;
    }

    private async Task<UserStatsResponse> GetUserStatsInternalAsync(int userId) {
        var messageCount = await _context.UserMessages
            .Where(um => um.UserId == userId)
            .CountAsync();

        var folderCount = await _context.Folders
            .Where(f => f.UserId == userId)
            .CountAsync();

        // Calculate storage used (sum of message sizes for this user)
        var storageUsed = await _context.UserMessages
            .Where(um => um.UserId == userId)
            .Join(_context.Messages, um => um.MessageId, m => m.Id, (um, m) => m.MessageSize)
            .SumAsync();

        var lastActivity = await _context.UserMessages
            .Where(um => um.UserId == userId)
            .OrderByDescending(um => um.ReceivedAt)
            .Select(um => um.ReceivedAt)
            .FirstOrDefaultAsync();

        return new UserStatsResponse {
            MessageCount = messageCount,
            StorageUsed = storageUsed,
            StorageUsedFormatted = FormatBytes(storageUsed),
            FolderCount = folderCount,
            LastActivity = lastActivity == default ? null : lastActivity
        };
    }

    private async Task CreateDefaultFoldersAsync(int userId) {
        var defaultFolders = new[] {
            new Folder { UserId = userId, Name = "INBOX", SystemFolderType = "INBOX" },
            new Folder { UserId = userId, Name = "SENT", SystemFolderType = "SENT" },
            new Folder { UserId = userId, Name = "DRAFTS", SystemFolderType = "DRAFTS" },
            new Folder { UserId = userId, Name = "TRASH", SystemFolderType = "TRASH" },
            new Folder { UserId = userId, Name = "SPAM", SystemFolderType = "SPAM" },
            new Folder { UserId = userId, Name = "OUTBOX", SystemFolderType = "OUTBOX" }
        };

        _context.Folders.AddRange(defaultFolders);
        await _context.SaveChangesAsync();
    }

    private UserResponse MapToUserResponse(Frimerki.Models.Entities.User user, UserStatsResponse stats) {
        return new UserResponse {
            Id = user.Id,
            Username = user.Username,
            Email = $"{user.Username}@{user.Domain.Name}",
            FullName = user.FullName,
            Role = user.Role,
            CanReceive = user.CanReceive,
            CanLogin = user.CanLogin,
            CreatedAt = user.CreatedAt,
            LastLogin = user.LastLogin,
            DomainName = user.Domain.Name,
            DomainId = user.DomainId,
            Stats = stats
        };
    }

    private string GenerateSalt() {
        var bytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }

    private string HashPassword(string password, string salt) {
        using var pbkdf2 = new Rfc2898DeriveBytes(password, Convert.FromBase64String(salt), 10000, HashAlgorithmName.SHA256);
        var hash = pbkdf2.GetBytes(32);
        return Convert.ToBase64String(hash);
    }

    private bool VerifyPassword(string password, string hash, string salt) {
        var hashToCheck = HashPassword(password, salt);
        return hashToCheck == hash;
    }

    private string FormatBytes(long bytes) {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1) {
            number /= 1024;
            counter++;
        }
        return $"{number:n1} {suffixes[counter]}";
    }
}

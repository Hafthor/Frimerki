using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using Frimerki.Data;
using Frimerki.Models.Configuration;
using Frimerki.Models.DTOs;
using Frimerki.Models.Entities;
using Frimerki.Services.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Frimerki.Services.User;

public partial class UserService : IUserService {
    private readonly EmailDbContext _context;
    private readonly INowProvider _nowProvider;
    private readonly ILogger<UserService> _logger;
    private readonly AccountLockoutOptions _lockoutOptions;

    public UserService(EmailDbContext context, INowProvider nowProvider, ILogger<UserService> logger, IOptions<AccountLockoutOptions> lockoutOptions) {
        _context = context;
        _nowProvider = nowProvider;
        _logger = logger;
        _lockoutOptions = lockoutOptions.Value;
    }

    public async Task<PaginatedInfo<UserResponse>> GetUsersAsync(int skip = 0, int take = 50, string? domainFilter = null) {
        _logger.LogInformation("Getting users list - Skip: {Skip}, Take: {Take}, Domain: {Domain}",
            skip, take, domainFilter ?? "All");

        var query = _context.Users
            .Include(u => u.Domain)
            .AsQueryable();

        if (!string.IsNullOrEmpty(domainFilter)) {
            query = query.Where(u => u.Domain.Name == domainFilter);
        }

        var totalCount = await query.CountAsync();

        var users = await query
            .OrderBy(u => u.Domain.Name)
            .ThenBy(u => u.Username)
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        List<UserResponse> userResponses = [];

        foreach (var user in users) {
            var stats = await GetUserStatsInternalAsync(user.Id);
            userResponses.Add(MapToUserResponse(user, stats));
        }

        return new PaginatedInfo<UserResponse> {
            Items = userResponses,
            Skip = skip,
            Take = take,
            TotalCount = totalCount
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
            CreatedAt = _nowProvider.UtcNow
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
            LastActivity = DateTime.MinValue
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

        // Reset failed login attempts when password is changed
        await ResetFailedLoginAttemptsAsync(user);

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
            _logger.LogWarning("Authentication failed for {Email}: User not found or cannot login", email);
            return null;
        }

        // Check if account is locked
        if (IsAccountLocked(user)) {
            _logger.LogWarning("Authentication failed for {Email}: Account is locked until {LockoutEnd}",
                email, user.LockoutEnd);
            return null;
        }

        if (!VerifyPassword(password, user.PasswordHash, user.Salt)) {
            _logger.LogWarning("Authentication failed for {Email}: Invalid password", email);
            await RecordFailedLoginAttemptAsync(user);
            return null;
        }

        // Successful login - reset failed attempts and update last login
        await ResetFailedLoginAttemptsAsync(user);
        user.LastLogin = _nowProvider.UtcNow;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Authentication successful for {Email}", email);
        var stats = await GetUserStatsInternalAsync(user.Id);
        return MapToUserResponse(user, stats);
    }

    public async Task<Frimerki.Models.Entities.User?> AuthenticateUserEntityAsync(string email, string password) {
        _logger.LogInformation("Authenticating user entity: {Email}", email);

        var user = await _context.Users
            .Include(u => u.Domain)
            .FirstOrDefaultAsync(u => u.Username + "@" + u.Domain.Name == email);

        if (user == null || !user.CanLogin) {
            _logger.LogWarning("Authentication failed for {Email}: User not found or cannot login", email);
            return null;
        }

        // Check if account is locked
        if (IsAccountLocked(user)) {
            _logger.LogWarning("Authentication failed for {Email}: Account is locked until {LockoutEnd}",
                email, user.LockoutEnd);
            return null;
        }

        if (!VerifyPassword(password, user.PasswordHash, user.Salt)) {
            _logger.LogWarning("Authentication failed for {Email}: Invalid password", email);
            await RecordFailedLoginAttemptAsync(user);
            return null;
        }

        // Successful login - reset failed attempts and update last login
        await ResetFailedLoginAttemptsAsync(user);
        user.LastLogin = _nowProvider.UtcNow;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Authentication successful for {Email}", email);
        return user;
    }

    public async Task<Frimerki.Models.Entities.User?> GetUserEntityByEmailAsync(string email) {
        _logger.LogInformation("Getting user entity by email: {Email}", email);

        return await _context.Users
            .Include(u => u.Domain)
            .FirstOrDefaultAsync(u => u.Username + "@" + u.Domain.Name == email);
    }

    [GeneratedRegex(@"^[a-zA-Z0-9._-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$")]
    private static partial Regex EmailValidationRegex();

    public Task<bool> ValidateEmailFormatAsync(string email) {
        return Task.FromResult(EmailValidationRegex().IsMatch(email));
    }

    public async Task<(bool IsLocked, DateTime? LockoutEnd)> GetAccountLockoutStatusAsync(string email) {
        var user = await _context.Users
            .Include(u => u.Domain)
            .FirstOrDefaultAsync(u => u.Username + "@" + u.Domain.Name == email);

        if (user == null) {
            return (false, null);
        }

        bool isLocked = IsAccountLocked(user);
        return (isLocked, user.LockoutEnd);
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
            LastActivity = lastActivity == default ? DateTime.MinValue : lastActivity
        };
    }

    private async Task CreateDefaultFoldersAsync(int userId) {
        var defaultFolders = new[] {
            new Models.Entities.Folder { UserId = userId, Name = "INBOX", SystemFolderType = "INBOX" },
            new Models.Entities.Folder { UserId = userId, Name = "SENT", SystemFolderType = "SENT" },
            new Models.Entities.Folder { UserId = userId, Name = "DRAFTS", SystemFolderType = "DRAFTS" },
            new Models.Entities.Folder { UserId = userId, Name = "TRASH", SystemFolderType = "TRASH" },
            new Models.Entities.Folder { UserId = userId, Name = "SPAM", SystemFolderType = "SPAM" },
            new Models.Entities.Folder { UserId = userId, Name = "OUTBOX", SystemFolderType = "OUTBOX" }
        };

        _context.Folders.AddRange(defaultFolders);
        await _context.SaveChangesAsync();
    }

    private UserResponse MapToUserResponse(Frimerki.Models.Entities.User user, UserStatsResponse stats) {
        return new UserResponse {
            Username = user.Username,
            Email = $"{user.Username}@{user.Domain.Name}",
            FullName = user.FullName,
            Role = user.Role,
            CanReceive = user.CanReceive,
            CanLogin = user.CanLogin,
            CreatedAt = user.CreatedAt,
            LastLogin = user.LastLogin ?? DateTime.MinValue,
            DomainName = user.Domain.Name,
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

    private bool IsAccountLocked(Frimerki.Models.Entities.User user) {
        if (!_lockoutOptions.Enabled || user.LockoutEnd == null) {
            return false;
        }

        var now = _nowProvider.UtcNow;
        if (user.LockoutEnd > now) {
            return true;
        }

        // Lockout period has expired, reset the user
        user.LockoutEnd = null;
        user.FailedLoginAttempts = 0;
        return false;
    }

    private async Task RecordFailedLoginAttemptAsync(Frimerki.Models.Entities.User user) {
        if (!_lockoutOptions.Enabled) {
            return;
        }

        var now = _nowProvider.UtcNow;

        // Reset failed attempts if the reset window has passed
        if (user.LastFailedLogin.HasValue &&
            now.Subtract(user.LastFailedLogin.Value).TotalMinutes > _lockoutOptions.ResetWindowMinutes) {
            user.FailedLoginAttempts = 0;
        }

        user.FailedLoginAttempts++;
        user.LastFailedLogin = now;

        // Lock account if threshold reached
        if (user.FailedLoginAttempts >= _lockoutOptions.MaxFailedAttempts) {
            user.LockoutEnd = now.AddMinutes(_lockoutOptions.LockoutDurationMinutes);
            _logger.LogWarning("Account locked for user {Email} after {Attempts} failed attempts. Lockout ends at {LockoutEnd}",
                $"{user.Username}@{user.Domain.Name}", user.FailedLoginAttempts, user.LockoutEnd);
        } else {
            _logger.LogWarning("Failed login attempt for user {Email}. Attempt {Current}/{Max}",
                $"{user.Username}@{user.Domain.Name}", user.FailedLoginAttempts, _lockoutOptions.MaxFailedAttempts);
        }

        await _context.SaveChangesAsync();
    }

    private async Task ResetFailedLoginAttemptsAsync(Frimerki.Models.Entities.User user) {
        if (user.FailedLoginAttempts > 0 || user.LockoutEnd.HasValue) {
            user.FailedLoginAttempts = 0;
            user.LockoutEnd = null;
            user.LastFailedLogin = null;
            await _context.SaveChangesAsync();
        }
    }
}

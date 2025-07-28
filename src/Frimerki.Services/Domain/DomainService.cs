using Frimerki.Data;
using Frimerki.Models.DTOs;
using Frimerki.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

namespace Frimerki.Services.Domain;

public interface IDomainService {
    Task<DomainListResponse> GetDomainsAsync(string? userRole = null, int? userDomainId = null);
    Task<DomainResponse> GetDomainByNameAsync(string domainName);
    Task<DomainResponse> CreateDomainAsync(DomainRequest request);
    Task<DomainResponse> UpdateDomainAsync(string domainName, DomainUpdateRequest request);
    Task DeleteDomainAsync(string domainName);
    Task<DkimKeyResponse> GetDkimKeyAsync(string domainName);
    Task<DkimKeyResponse> GenerateDkimKeyAsync(string domainName, GenerateDkimKeyRequest request);
}

public class DomainService : IDomainService {
    private readonly EmailDbContext _dbContext;
    private readonly ILogger<DomainService> _logger;

    public DomainService(EmailDbContext dbContext, ILogger<DomainService> logger) {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<DomainListResponse> GetDomainsAsync(string? userRole = null, int? userDomainId = null) {
        var query = _dbContext.Domains.AsQueryable();

        // Apply role-based filtering
        if (userRole == "DomainAdmin" && userDomainId.HasValue) {
            query = query.Where(d => d.Id == userDomainId.Value);
        }

        var domains = await query
            .Include(d => d.CatchAllUser)
            .Include(d => d.DkimKeys)
            .Include(d => d.Users)
            .OrderBy(d => d.Name)
            .ToListAsync();

        var domainResponses = new List<DomainResponse>();

        foreach (var domain in domains) {
            var userCount = domain.Users?.Count ?? 0;
            var storageUsed = await CalculateDomainStorageAsync(domain.Id);
            var activeDkimKey = domain.DkimKeys?.FirstOrDefault(k => k.IsActive);

            domainResponses.Add(new DomainResponse {
                Id = domain.Id,
                Name = domain.Name,
                IsActive = domain.IsActive,
                CatchAllUser = domain.CatchAllUser != null
                    ? $"{domain.CatchAllUser.Username}@{domain.Name}"
                    : null,
                CreatedAt = domain.CreatedAt,
                UserCount = userCount,
                StorageUsed = storageUsed,
                DkimKey = activeDkimKey != null ? new DkimKeyInfo {
                    Selector = activeDkimKey.Selector,
                    PublicKey = activeDkimKey.PublicKey,
                    IsActive = activeDkimKey.IsActive,
                    CreatedAt = activeDkimKey.CreatedAt
                } : null
            });
        }

        return new DomainListResponse {
            Domains = domainResponses,
            TotalCount = domainResponses.Count,
            CanManageAll = userRole == "HostAdmin"
        };
    }

    public async Task<DomainResponse> GetDomainByNameAsync(string domainName) {
        var domain = await _dbContext.Domains
            .Include(d => d.CatchAllUser)
            .Include(d => d.DkimKeys)
            .Include(d => d.Users)
            .FirstOrDefaultAsync(d => d.Name.ToLower() == domainName.ToLower());

        if (domain == null) {
            throw new ArgumentException($"Domain '{domainName}' not found");
        }

        var userCount = domain.Users?.Count ?? 0;
        var storageUsed = await CalculateDomainStorageAsync(domain.Id);
        var activeDkimKey = domain.DkimKeys?.FirstOrDefault(k => k.IsActive);

        return new DomainResponse {
            Id = domain.Id,
            Name = domain.Name,
            IsActive = domain.IsActive,
            CatchAllUser = domain.CatchAllUser != null
                ? $"{domain.CatchAllUser.Username}@{domain.Name}"
                : null,
            CreatedAt = domain.CreatedAt,
            UserCount = userCount,
            StorageUsed = storageUsed,
            DkimKey = activeDkimKey != null ? new DkimKeyInfo {
                Selector = activeDkimKey.Selector,
                PublicKey = activeDkimKey.PublicKey,
                IsActive = activeDkimKey.IsActive,
                CreatedAt = activeDkimKey.CreatedAt
            } : null
        };
    }

    public async Task<DomainResponse> CreateDomainAsync(DomainRequest request) {
        // Check if domain already exists
        var existingDomain = await _dbContext.Domains
            .FirstOrDefaultAsync(d => d.Name.ToLower() == request.Name.ToLower());

        if (existingDomain != null) {
            throw new InvalidOperationException($"Domain '{request.Name}' already exists");
        }

        // Validate catch-all user if specified
        if (!string.IsNullOrEmpty(request.CatchAllUser)) {
            var emailParts = request.CatchAllUser.Split('@');
            if (emailParts.Length != 2 || emailParts[1].ToLower() != request.Name.ToLower()) {
                throw new ArgumentException("Catch-all user must belong to the domain being created");
            }

            // Note: We'll validate the user exists after domain creation when implementing user management
        }

        var domain = new Models.Entities.Domain {
            Name = request.Name.ToLower(),
            IsActive = request.IsActive,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Domains.Add(domain);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Domain '{DomainName}' created with ID {DomainId}", domain.Name, domain.Id);

        return new DomainResponse {
            Id = domain.Id,
            Name = domain.Name,
            IsActive = domain.IsActive,
            CatchAllUser = request.CatchAllUser,
            CreatedAt = domain.CreatedAt,
            UserCount = 0,
            StorageUsed = 0,
            DkimKey = null
        };
    }

    public async Task<DomainResponse> UpdateDomainAsync(string domainName, DomainUpdateRequest request) {
        var domain = await _dbContext.Domains
            .Include(d => d.CatchAllUser)
            .Include(d => d.Users)
            .FirstOrDefaultAsync(d => d.Name.ToLower() == domainName.ToLower());

        if (domain == null) {
            throw new ArgumentException($"Domain '{domainName}' not found");
        }

        var hasChanges = false;

        // Update IsActive if provided
        if (request.IsActive.HasValue && domain.IsActive != request.IsActive.Value) {
            domain.IsActive = request.IsActive.Value;
            hasChanges = true;
            _logger.LogInformation("Domain '{DomainName}' IsActive changed to {IsActive}", domainName, request.IsActive.Value);
        }

        // Update catch-all user if provided
        if (request.CatchAllUser != domain.CatchAllUser?.Username + "@" + domain.Name) {
            if (string.IsNullOrEmpty(request.CatchAllUser)) {
                domain.CatchAllUserId = null;
                hasChanges = true;
            } else {
                var emailParts = request.CatchAllUser.Split('@');
                if (emailParts.Length != 2 || emailParts[1].ToLower() != domain.Name.ToLower()) {
                    throw new ArgumentException("Catch-all user must belong to this domain");
                }

                var catchAllUser = await _dbContext.Users
                    .FirstOrDefaultAsync(u => u.Username.ToLower() == emailParts[0].ToLower() && u.DomainId == domain.Id);

                if (catchAllUser == null) {
                    throw new ArgumentException($"User '{emailParts[0]}' not found in domain '{domain.Name}'");
                }

                domain.CatchAllUserId = catchAllUser.Id;
                hasChanges = true;
            }
        }

        if (hasChanges) {
            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Domain '{DomainName}' updated", domainName);
        }

        return await GetDomainByNameAsync(domainName);
    }

    public async Task DeleteDomainAsync(string domainName) {
        var domain = await _dbContext.Domains
            .Include(d => d.Users)
            .Include(d => d.DkimKeys)
            .FirstOrDefaultAsync(d => d.Name.ToLower() == domainName.ToLower());

        if (domain == null) {
            throw new ArgumentException($"Domain '{domainName}' not found");
        }

        // Check if domain has users
        if (domain.Users?.Any() == true) {
            throw new InvalidOperationException($"Cannot delete domain '{domainName}' because it has {domain.Users.Count} user(s)");
        }

        _dbContext.Domains.Remove(domain);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Domain '{DomainName}' deleted", domainName);
    }

    public async Task<DkimKeyResponse> GetDkimKeyAsync(string domainName) {
        var domain = await _dbContext.Domains
            .Include(d => d.DkimKeys)
            .FirstOrDefaultAsync(d => d.Name.ToLower() == domainName.ToLower());

        if (domain == null) {
            throw new ArgumentException($"Domain '{domainName}' not found");
        }

        var dkimKey = domain.DkimKeys?.FirstOrDefault(k => k.IsActive);
        if (dkimKey == null) {
            throw new ArgumentException($"No active DKIM key found for domain '{domainName}'");
        }

        var dnsRecord = $"v=DKIM1; k=rsa; p={dkimKey.PublicKey}";

        return new DkimKeyResponse {
            Selector = dkimKey.Selector,
            PublicKey = dkimKey.PublicKey,
            DnsRecord = dnsRecord,
            IsActive = dkimKey.IsActive,
            CreatedAt = dkimKey.CreatedAt
        };
    }

    public async Task<DkimKeyResponse> GenerateDkimKeyAsync(string domainName, GenerateDkimKeyRequest request) {
        var domain = await _dbContext.Domains
            .Include(d => d.DkimKeys)
            .FirstOrDefaultAsync(d => d.Name.ToLower() == domainName.ToLower());

        if (domain == null) {
            throw new ArgumentException($"Domain '{domainName}' not found");
        }

        // Check if selector already exists
        var existingKey = domain.DkimKeys?.FirstOrDefault(k => k.Selector == request.Selector);
        if (existingKey != null) {
            throw new InvalidOperationException($"DKIM key with selector '{request.Selector}' already exists for domain '{domainName}'");
        }

        // Generate RSA key pair
        var (privateKey, publicKey) = GenerateRsaKeyPair(request.KeySize);

        // Deactivate existing keys
        if (domain.DkimKeys != null) {
            foreach (var key in domain.DkimKeys) {
                key.IsActive = false;
            }
        }

        // Create new DKIM key
        var dkimKey = new DkimKey {
            DomainId = domain.Id,
            Selector = request.Selector,
            PrivateKey = privateKey,
            PublicKey = publicKey,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.DkimKeys.Add(dkimKey);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("DKIM key generated for domain '{DomainName}' with selector '{Selector}'", domainName, request.Selector);

        var dnsRecord = $"v=DKIM1; k=rsa; p={publicKey}";

        return new DkimKeyResponse {
            Selector = dkimKey.Selector,
            PublicKey = dkimKey.PublicKey,
            DnsRecord = dnsRecord,
            IsActive = dkimKey.IsActive,
            CreatedAt = dkimKey.CreatedAt
        };
    }

    private async Task<long> CalculateDomainStorageAsync(int domainId) {
        // Calculate storage used by all users in the domain
        // This is a simplified calculation - in a real implementation,
        // you'd sum up message sizes and attachment sizes
        var messageCount = await _dbContext.Users
            .Where(u => u.DomainId == domainId)
            .Join(_dbContext.UserMessages, u => u.Id, um => um.UserId, (u, um) => um)
            .Join(_dbContext.Messages, um => um.MessageId, m => m.Id, (um, m) => m.MessageSize)
            .SumAsync();

        return messageCount;
    }

    private static (string privateKey, string publicKey) GenerateRsaKeyPair(int keySize) {
        using var rsa = RSA.Create(keySize);

        // Export private key in PKCS#8 format
        var privateKeyBytes = rsa.ExportPkcs8PrivateKey();
        var privateKey = Convert.ToBase64String(privateKeyBytes);

        // Export public key for DKIM (remove headers and newlines)
        var publicKeyInfo = rsa.ExportRSAPublicKey();
        var publicKey = Convert.ToBase64String(publicKeyInfo);

        // Format private key with PEM headers
        var formattedPrivateKey = $"-----BEGIN PRIVATE KEY-----\n{FormatBase64(privateKey)}\n-----END PRIVATE KEY-----";

        return (formattedPrivateKey, publicKey);
    }

    private static string FormatBase64(string base64String) {
        var result = new StringBuilder();
        for (int i = 0; i < base64String.Length; i += 64) {
            var length = Math.Min(64, base64String.Length - i);
            result.AppendLine(base64String.Substring(i, length));
        }
        return result.ToString().Trim();
    }
}

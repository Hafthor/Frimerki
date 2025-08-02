using System.Security.Cryptography;
using System.Text;
using Frimerki.Data;
using Frimerki.Models.DTOs;
using Frimerki.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Frimerki.Services.Domain;

public interface IDomainService {
    Task<DomainListResponse> GetDomainsAsync(string userRole = "", int userDomainId = 0);
    Task<DomainResponse> GetDomainByNameAsync(string domainName);
    Task<CreateDomainResponse> CreateDomainAsync(DomainRequest request);
    Task<DomainResponse> UpdateDomainAsync(string domainName, DomainUpdateRequest request);
    Task DeleteDomainAsync(string domainName);
    Task<DkimKeyResponse> GetDkimKeyAsync(string domainName);
    Task<DkimKeyResponse> GenerateDkimKeyAsync(string domainName, GenerateDkimKeyRequest request);
}

public class DomainService : IDomainService {
    private readonly EmailDbContext _dbContext;
    private readonly IDomainRegistryService _domainRegistryService;
    private readonly IDomainDbContextFactory _domainDbFactory;
    private readonly ILogger<DomainService> _logger;

    public DomainService(
        EmailDbContext dbContext,
        IDomainRegistryService domainRegistryService,
        IDomainDbContextFactory domainDbFactory,
        ILogger<DomainService> logger) {
        _dbContext = dbContext;
        _domainRegistryService = domainRegistryService;
        _domainDbFactory = domainDbFactory;
        _logger = logger;
    }

    public async Task<DomainListResponse> GetDomainsAsync(string userRole = "", int userDomainId = 0) {
        var query = _dbContext.Domains.AsQueryable();

        // Apply role-based filtering
        if (userRole == "DomainAdmin" && userDomainId > 0) {
            query = query.Where(d => d.Id == userDomainId);
        }

        var domains = await query
            .Include(d => d.CatchAllUser)
            .Include(d => d.DkimKeys)
            .Include(d => d.Users)
            .OrderBy(d => d.Name)
            .ToListAsync();

        List<DomainResponse> domainResponses = [];

        foreach (var domain in domains) {
            var userCount = domain.Users?.Count ?? 0;
            var storageUsed = await CalculateDomainStorageAsync(domain.Id);
            var activeDkimKey = domain.DkimKeys?.FirstOrDefault(k => k.IsActive);

            domainResponses.Add(new DomainResponse {
                Name = domain.Name,
                IsActive = await GetDomainIsActiveAsync(domain.Name),
                DatabaseName = "", // Will be populated from registry if needed
                IsDedicated = false, // Will be calculated if needed
                CatchAllUser = domain.CatchAllUser != null
                    ? $"{domain.CatchAllUser.Username}@{domain.Name}"
                    : null,
                CreatedAt = domain.CreatedAt,
                UserCount = userCount,
                StorageUsed = storageUsed,
                HasDkim = activeDkimKey != null,
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
            .FirstOrDefaultAsync(d => d.Name.Equals(domainName, StringComparison.OrdinalIgnoreCase));

        if (domain == null) {
            throw new ArgumentException($"Domain '{domainName}' not found");
        }

        var storageUsed = await CalculateDomainStorageAsync(domain.Id);
        var activeDkimKey = domain.DkimKeys?.FirstOrDefault(k => k.IsActive);

        return new DomainResponse {
            Name = domain.Name,
            IsActive = await GetDomainIsActiveAsync(domain.Name),
            DatabaseName = "", // Will be populated from registry if needed
            IsDedicated = false, // Will be calculated if needed
            CatchAllUser = domain.CatchAllUser == null ? null :
                $"{domain.CatchAllUser.Username}@{domain.Name}",
            CreatedAt = domain.CreatedAt,
            UserCount = domain.Users?.Count ?? 0,
            StorageUsed = storageUsed,
            HasDkim = activeDkimKey != null,
            DkimKey = activeDkimKey == null ? null : new DkimKeyInfo {
                Selector = activeDkimKey.Selector,
                PublicKey = activeDkimKey.PublicKey,
                IsActive = activeDkimKey.IsActive,
                CreatedAt = activeDkimKey.CreatedAt
            }
        };
    }

    public async Task<CreateDomainResponse> CreateDomainAsync(DomainRequest request) {
        // Check if domain already exists in domain registry
        var existingRegistry = await _domainRegistryService.GetDomainRegistryAsync(request.Name);
        if (existingRegistry != null) {
            throw new InvalidOperationException($"Domain '{request.Name}' is already registered in the domain registry");
        }

        // Validate catch-all user if specified
        if (!string.IsNullOrEmpty(request.CatchAllUser)) {
            var emailParts = request.CatchAllUser.Split('@');
            if (emailParts.Length != 2 || !emailParts[1].Equals(request.Name, StringComparison.OrdinalIgnoreCase)) {
                throw new ArgumentException("Catch-all user must belong to the domain being created");
            }
        }

        var normalizedDomainName = request.Name.ToLower();

        try {
            // Step 1: Register domain in the domain registry (this creates the domain-specific database)
            var domainRegistry = await _domainRegistryService.RegisterDomainAsync(
                normalizedDomainName,
                request.DatabaseName,
                request.CreateDatabase);
            _logger.LogInformation("Domain '{DomainName}' registered in domain registry with database '{DatabaseName}'",
                normalizedDomainName, domainRegistry.DatabaseName);

            // Step 2: Create domain settings in the domain-specific database
            using var domainContext = await _domainDbFactory.CreateDbContextAsync(normalizedDomainName);
            var domainSettings = new Models.Entities.DomainSettings {
                Name = normalizedDomainName,
                CreatedAt = DateTime.UtcNow
            };

            domainContext.DomainSettings.Add(domainSettings);
            await domainContext.SaveChangesAsync();

            _logger.LogInformation("Domain '{DomainName}' created in domain-specific database with ID {DomainId}",
                domainSettings.Name, domainSettings.Id);

            // Determine if database is dedicated (only used by this domain)
            var allDomains = await _domainRegistryService.GetAllDomainsAsync();
            var isDedicated = allDomains.Count(d => d.DatabaseName == domainRegistry.DatabaseName) == 1;

            return new CreateDomainResponse {
                Name = domainSettings.Name,
                DatabaseName = domainRegistry.DatabaseName,
                IsActive = domainRegistry.IsActive,
                IsDedicated = isDedicated,
                CreatedAt = domainSettings.CreatedAt,
                DatabaseCreated = request.CreateDatabase || request.DatabaseName == null,
                InitialSetup = new InitialSetupInfo {
                    AdminUserCreated = false,
                    DefaultFoldersCreated = true,
                    DkimKeysGenerated = false
                }
            };
        } catch (Exception ex) {
            _logger.LogError(ex, "Failed to create domain '{DomainName}'", normalizedDomainName);
            throw;
        }
    }

    public async Task<DomainResponse> UpdateDomainAsync(string domainName, DomainUpdateRequest request) {
        var domain = await _dbContext.Domains
            .Include(d => d.CatchAllUser)
            .Include(d => d.Users)
            .FirstOrDefaultAsync(d => d.Name.Equals(domainName, StringComparison.OrdinalIgnoreCase));

        if (domain == null) {
            throw new ArgumentException($"Domain '{domainName}' not found");
        }

        var hasChanges = false;

        // Update Name if provided
        if (!string.IsNullOrEmpty(request.Name) && domain.Name != request.Name) {
            // Check if new name already exists
            var existingDomain = await _dbContext.Domains
                .FirstOrDefaultAsync(d => d.Name.Equals(request.Name, StringComparison.OrdinalIgnoreCase) && d.Id != domain.Id);

            if (existingDomain != null) {
                throw new InvalidOperationException($"Domain '{request.Name}' already exists");
            }

            domain.Name = request.Name;
            hasChanges = true;
            _logger.LogInformation("Domain '{DomainName}' name changed to '{NewName}'", domainName, request.Name);
        }

        // Update IsActive if provided - this updates the global registry, not domain settings
        if (request.IsActive.HasValue) {
            await _domainRegistryService.SetDomainActiveAsync(domainName, request.IsActive.Value);
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
                if (emailParts.Length != 2 || !emailParts[1].Equals(domain.Name, StringComparison.OrdinalIgnoreCase)) {
                    throw new ArgumentException("Catch-all user must belong to this domain");
                }

                var catchAllUser = await _dbContext.Users
                    .FirstOrDefaultAsync(u => u.Username.Equals(emailParts[0], StringComparison.OrdinalIgnoreCase) && u.DomainId == domain.Id);

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

        return await GetDomainByNameAsync(domain.Name);
    }

    public async Task DeleteDomainAsync(string domainName) {
        var domain = await _dbContext.Domains
            .Include(d => d.Users)
            .Include(d => d.DkimKeys)
            .FirstOrDefaultAsync(d => d.Name.Equals(domainName, StringComparison.OrdinalIgnoreCase));

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
            .FirstOrDefaultAsync(d => d.Name.Equals(domainName, StringComparison.OrdinalIgnoreCase));

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
            .FirstOrDefaultAsync(d => d.Name.Equals(domainName, StringComparison.OrdinalIgnoreCase));

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

        // Export public key for DKIM (remove headers and newlines)
        var publicKeyInfo = rsa.ExportRSAPublicKey();
        var publicKey = Convert.ToBase64String(publicKeyInfo);

        // Export private key in PKCS#8 format
        var privateKeyBytes = rsa.ExportPkcs8PrivateKey();
        var privateKey = Convert.ToBase64String(privateKeyBytes);
        var formattedPrivateKey = FormatBase64PrivateKey(privateKey);

        return (formattedPrivateKey, publicKey);
    }

    // Format private key with PEM headers
    private static string FormatBase64PrivateKey(string base64String) {
        StringBuilder result = new();
        result.AppendLine("-----BEGIN PRIVATE KEY-----");
        for (int i = 0; i < base64String.Length; i += 64) {
            var length = Math.Min(64, base64String.Length - i);
            result.AppendLine(base64String.Substring(i, length));
        }
        return result.Append("-----END PRIVATE KEY-----").ToString();
    }

    private async Task<bool> GetDomainIsActiveAsync(string domainName) {
        var domainRegistry = await _domainRegistryService.GetDomainRegistryAsync(domainName);
        return domainRegistry?.IsActive ?? false;
    }
}

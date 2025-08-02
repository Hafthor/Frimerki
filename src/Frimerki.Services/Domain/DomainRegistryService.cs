using Frimerki.Data;
using Frimerki.Models.Entities;
using Frimerki.Services.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Frimerki.Services.Domain;

/// <summary>
/// Service for managing domain registration and database creation
/// </summary>
public interface IDomainRegistryService {
    Task<DomainRegistry> RegisterDomainAsync(string domainName, string databaseName = "", bool createDatabase = false);
    Task<DomainRegistry> GetDomainRegistryAsync(string domainName);
    Task<List<DomainRegistry>> GetAllDomainsAsync();
    Task<bool> DomainExistsAsync(string domainName);
    Task SetDomainActiveAsync(string domainName, bool isActive);
}

public class DomainRegistryService : IDomainRegistryService {
    private readonly GlobalDbContext _globalContext;
    private readonly IDomainDbContextFactory _domainDbFactory;
    private readonly INowProvider _nowProvider;
    private readonly ILogger<DomainRegistryService> _logger;

    public DomainRegistryService(
        GlobalDbContext globalContext,
        IDomainDbContextFactory domainDbFactory,
        INowProvider nowProvider,
        ILogger<DomainRegistryService> logger) {
        _globalContext = globalContext;
        _domainDbFactory = domainDbFactory;
        _nowProvider = nowProvider;
        _logger = logger;
    }

    public async Task<DomainRegistry> RegisterDomainAsync(string domainName, string databaseName = "", bool createDatabase = false) {
        var normalizedDomain = domainName.ToLower();

        // Check if domain already exists
        var existing = await _globalContext.DomainRegistry
            .FirstOrDefaultAsync(d => d.Name == normalizedDomain);

        if (existing != null) {
            throw new InvalidOperationException($"Domain {domainName} is already registered");
        }

        // Determine database name
        var targetDatabaseName = databaseName ?? $"domain_{normalizedDomain.Replace(".", "_")}";

        // Check database existence
        var databaseExists = await DatabaseExistsAsync(targetDatabaseName);

        if (createDatabase && databaseExists) {
            throw new InvalidOperationException($"Database {targetDatabaseName} already exists");
        }

        if (!createDatabase && !databaseExists) {
            throw new InvalidOperationException($"Database {targetDatabaseName} does not exist");
        }

        // Create database if needed
        if (createDatabase || !databaseExists) {
            await _domainDbFactory.EnsureDatabaseExistsAsync(targetDatabaseName);
        }

        // Register in global database
        var registry = new DomainRegistry {
            Name = normalizedDomain,
            DatabaseName = targetDatabaseName,
            IsActive = true,
            CreatedAt = _nowProvider.UtcNow
        };

        _globalContext.DomainRegistry.Add(registry);
        await _globalContext.SaveChangesAsync();

        _logger.LogInformation("Domain {DomainName} registered successfully with database {DatabaseName}",
            domainName, targetDatabaseName);
        return registry;
    }

    public async Task<DomainRegistry> GetDomainRegistryAsync(string domainName) {
        var normalizedDomain = domainName.ToLower();
        return await _globalContext.DomainRegistry
            .FirstOrDefaultAsync(d => d.Name == normalizedDomain);
    }

    public async Task<List<DomainRegistry>> GetAllDomainsAsync() {
        return await _globalContext.DomainRegistry
            .Where(d => d.IsActive)
            .OrderBy(d => d.Name)
            .ToListAsync();
    }

    public async Task<bool> DomainExistsAsync(string domainName) {
        var normalizedDomain = domainName.ToLower();
        return await _globalContext.DomainRegistry
            .AnyAsync(d => d.Name == normalizedDomain && d.IsActive);
    }

    public async Task SetDomainActiveAsync(string domainName, bool isActive) {
        var normalizedDomain = domainName.ToLower();
        var registry = await _globalContext.DomainRegistry
            .FirstOrDefaultAsync(d => d.Name == normalizedDomain);

        if (registry != null) {
            registry.IsActive = isActive;

            await _globalContext.SaveChangesAsync();

            _logger.LogInformation("Domain {DomainName} set to {Status}",
                domainName, isActive ? "active" : "inactive");
        }
    }

    private async Task<bool> DatabaseExistsAsync(string databaseName) {
        return await _globalContext.DomainRegistry
            .AnyAsync(d => d.DatabaseName == databaseName);
    }
}

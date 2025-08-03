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

public class DomainRegistryService(
    GlobalDbContext globalContext,
    IDomainDbContextFactory domainDbFactory,
    INowProvider nowProvider,
    ILogger<DomainRegistryService> logger)
    : IDomainRegistryService {
    public async Task<DomainRegistry> RegisterDomainAsync(string domainName, string databaseName = "", bool createDatabase = false) {
        var normalizedDomain = domainName.ToLower();

        // Check if domain already exists
        var existing = await globalContext.DomainRegistry
            .FirstOrDefaultAsync(d => d.Name == normalizedDomain);

        if (existing != null) {
            throw new InvalidOperationException($"Domain {domainName} is already registered");
        }

        // Determine database name
        var targetDatabaseName = databaseName ?? $"domain_{normalizedDomain.Replace(".", "_")}";

        // Check database existence
        var databaseExists = await DatabaseExistsAsync(targetDatabaseName);

        if (createDatabase) {
            if (databaseExists) {
                throw new InvalidOperationException($"Database {targetDatabaseName} already exists");
            }
            // Create database if needed
            await domainDbFactory.EnsureDatabaseExistsAsync(targetDatabaseName);
        } else if (!databaseExists) {
            throw new InvalidOperationException($"Database {targetDatabaseName} does not exist");
        }

        // Register in global database
        var registry = new DomainRegistry {
            Name = normalizedDomain,
            DatabaseName = targetDatabaseName,
            IsActive = true,
            CreatedAt = nowProvider.UtcNow
        };

        globalContext.DomainRegistry.Add(registry);
        await globalContext.SaveChangesAsync();

        logger.LogInformation("Domain {DomainName} registered successfully with database {DatabaseName}",
            domainName, targetDatabaseName);
        return registry;
    }

    public async Task<DomainRegistry> GetDomainRegistryAsync(string domainName) {
        var normalizedDomain = domainName.ToLower();
        return await globalContext.DomainRegistry
            .FirstOrDefaultAsync(d => d.Name == normalizedDomain);
    }

    public async Task<List<DomainRegistry>> GetAllDomainsAsync() {
        return await globalContext.DomainRegistry
            .Where(d => d.IsActive)
            .OrderBy(d => d.Name)
            .ToListAsync();
    }

    public async Task<bool> DomainExistsAsync(string domainName) {
        var normalizedDomain = domainName.ToLower();
        return await globalContext.DomainRegistry
            .AnyAsync(d => d.Name == normalizedDomain && d.IsActive);
    }

    public async Task SetDomainActiveAsync(string domainName, bool isActive) {
        var normalizedDomain = domainName.ToLower();
        var registry = await globalContext.DomainRegistry
            .FirstOrDefaultAsync(d => d.Name == normalizedDomain);

        if (registry != null) {
            registry.IsActive = isActive;

            await globalContext.SaveChangesAsync();

            logger.LogInformation("Domain {DomainName} set to {Status}",
                domainName, isActive ? "active" : "inactive");
        }
    }

    private async Task<bool> DatabaseExistsAsync(string databaseName) {
        return await globalContext.DomainRegistry
            .AnyAsync(d => d.DatabaseName == databaseName);
    }
}

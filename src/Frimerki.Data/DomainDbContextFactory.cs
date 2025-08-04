using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Frimerki.Data;

/// <summary>
/// Factory for creating domain-specific database contexts
/// </summary>
public interface IDomainDbContextFactory {
    DomainDbContext CreateDbContext(string domainName);
    Task<DomainDbContext> CreateDbContextAsync(string domainName);
    Task EnsureDatabaseExistsAsync(string domainName);
}

public class DomainDbContextFactory : IDomainDbContextFactory {
    private readonly ILogger<DomainDbContextFactory> _logger;
    private readonly string _databaseDirectory;

    public DomainDbContextFactory(IConfiguration configuration, ILogger<DomainDbContextFactory> logger) {
        _logger = logger;

        // Get the base directory for domain databases
        var baseDir = configuration.GetConnectionString("DatabaseDirectory") ?? "Data";
        _databaseDirectory = Path.GetFullPath(baseDir);

        // Ensure the directory exists
        Directory.CreateDirectory(_databaseDirectory);
    }

    private string GetDatabasePath(string domainName) {
        // Sanitize domain name for file system
        var sanitizedDomain = string.Join("_", domainName.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_databaseDirectory, $"domain_{sanitizedDomain}.db");
    }

    public DomainDbContext CreateDbContext(string domainName) {
        var dbPath = GetDatabasePath(domainName);
        var optionsBuilder = new DbContextOptionsBuilder<DomainDbContext>();
        optionsBuilder.UseSqlite($"Data Source={dbPath}");

        return new DomainDbContext(optionsBuilder.Options, domainName);
    }

    public async Task<DomainDbContext> CreateDbContextAsync(string domainName) {
        await EnsureDatabaseExistsAsync(domainName);
        return CreateDbContext(domainName);
    }

    public async Task EnsureDatabaseExistsAsync(string domainName) {
        var dbPath = GetDatabasePath(domainName);
        if (File.Exists(dbPath)) {
            return;
        }

        _logger.LogInformation("Creating new domain database for {DomainName} at {DatabasePath}", domainName, dbPath);

        await using var context = await CreateDbContextAsync(domainName);
        await context.Database.EnsureCreatedAsync();

        // Create the domain record in the domain-specific database
        var domain = new Models.Entities.DomainSettings {
            Name = domainName,
            CreatedAt = DateTime.UtcNow
        };

        context.DomainSettings.Add(domain);
        await context.SaveChangesAsync();

        _logger.LogInformation("Domain database created successfully for {DomainName}", domainName);
    }
}

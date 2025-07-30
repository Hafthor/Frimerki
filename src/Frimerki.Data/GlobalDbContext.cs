using Frimerki.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Frimerki.Data;

/// <summary>
/// Global database context for server-wide data including domain registry and host admin authentication
/// </summary>
public class GlobalDbContext : DbContext {
    public GlobalDbContext(DbContextOptions<GlobalDbContext> options) : base(options) {
    }

    public DbSet<DomainRegistry> DomainRegistry { get; set; }
    public DbSet<HostAdmin> HostAdmins { get; set; }
    public DbSet<ServerConfiguration> ServerConfiguration { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder) {
        base.OnModelCreating(modelBuilder);

        // Configure DomainRegistry entity
        modelBuilder.Entity<DomainRegistry>(entity => {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Name).IsUnique();
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255);
            entity.Property(e => e.DatabaseName).IsRequired().HasMaxLength(255);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("datetime('now')");
        });

        // Configure HostAdmin entity
        modelBuilder.Entity<HostAdmin>(entity => {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Username).IsUnique();
            entity.Property(e => e.Username).IsRequired().HasMaxLength(255);
            entity.Property(e => e.PasswordHash).IsRequired().HasMaxLength(255);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("datetime('now')");
            entity.Property(e => e.LastLoginAt);
        });

        // Configure ServerConfiguration entity
        modelBuilder.Entity<ServerConfiguration>(entity => {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Key).IsUnique();
            entity.Property(e => e.Key).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Value).HasMaxLength(2000);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.ModifiedAt).HasDefaultValueSql("datetime('now')");
        });
    }
}

using Frimerki.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Frimerki.Data;

public class EmailDbContext(DbContextOptions<EmailDbContext> options) : DbContext(options) {
    public DbSet<User> Users { get; set; }
    public DbSet<DomainSettings> Domains { get; set; }
    public DbSet<Message> Messages { get; set; }
    public DbSet<Folder> Folders { get; set; }
    public DbSet<UserMessage> UserMessages { get; set; }
    public DbSet<MessageFlag> MessageFlags { get; set; }
    public DbSet<Attachment> Attachments { get; set; }
    public DbSet<UidValiditySequence> UidValiditySequences { get; set; }
    public DbSet<DkimKey> DkimKeys { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder) {
        base.OnModelCreating(modelBuilder);

        // Configure User entity
        modelBuilder.Entity<User>(entity => {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.Username, e.DomainId }).IsUnique();
            entity.HasOne(e => e.Domain)
                  .WithMany(d => d.Users)
                  .HasForeignKey(e => e.DomainId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure DomainSettings entity
        modelBuilder.Entity<DomainSettings>(entity => {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Name).IsUnique();
            entity.HasOne(e => e.CatchAllUser)
                  .WithMany()
                  .HasForeignKey(e => e.CatchAllUserId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        // Configure Message entity
        modelBuilder.Entity<Message>(entity => {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.HeaderMessageId).IsUnique();
            entity.HasIndex(e => e.Uid).IsUnique();
            entity.HasOne(e => e.UidValiditySequence)
                  .WithMany(u => u.Messages)
                  .HasForeignKey(e => e.UidValidity)
                  .HasPrincipalKey(u => u.Value)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        // Configure Folder entity
        modelBuilder.Entity<Folder>(entity => {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.Name }).IsUnique();
            entity.HasIndex(e => new { e.UserId, e.SystemFolderType })
                  .IsUnique()
                  .HasFilter("[SystemFolderType] IS NOT NULL");
            entity.HasOne(e => e.User)
                  .WithMany(u => u.Folders)
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure UserMessage entity
        modelBuilder.Entity<UserMessage>(entity => {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.FolderId, e.Uid }).IsUnique();
            entity.HasIndex(e => new { e.UserId, e.MessageId, e.FolderId }).IsUnique();
            entity.HasOne(e => e.User)
                  .WithMany(u => u.UserMessages)
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Message)
                  .WithMany(m => m.UserMessages)
                  .HasForeignKey(e => e.MessageId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Folder)
                  .WithMany(f => f.UserMessages)
                  .HasForeignKey(e => e.FolderId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure MessageFlag entity
        modelBuilder.Entity<MessageFlag>(entity => {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.MessageId, e.UserId, e.FlagName }).IsUnique();
            entity.HasOne(e => e.Message)
                  .WithMany(m => m.MessageFlags)
                  .HasForeignKey(e => e.MessageId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.User)
                  .WithMany(u => u.MessageFlags)
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure Attachment entity
        modelBuilder.Entity<Attachment>(entity => {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.FileGuid).IsUnique();
            entity.HasOne(e => e.Message)
                  .WithMany(m => m.Attachments)
                  .HasForeignKey(e => e.MessageId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure UidValiditySequence entity
        modelBuilder.Entity<UidValiditySequence>(entity => {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.DomainId, e.Value }).IsUnique();
            entity.HasOne(e => e.Domain)
                  .WithMany(d => d.UidValiditySequences)
                  .HasForeignKey(e => e.DomainId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure DkimKey entity
        modelBuilder.Entity<DkimKey>(entity => {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.DomainId, e.Selector }).IsUnique();
            entity.HasOne(e => e.Domain)
                  .WithMany(d => d.DkimKeys)
                  .HasForeignKey(e => e.DomainId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

using Microsoft.EntityFrameworkCore;
using PresenceTracker.Models;

namespace PresenceTracker.Data;

public class PresenceDbContext : DbContext
{
    public PresenceDbContext(DbContextOptions<PresenceDbContext> options) : base(options) { }

    public DbSet<PresenceChange> PresenceChanges => Set<PresenceChange>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PresenceChange>(entity =>
        {
            entity.ToTable("PresenceChanges");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).UseIdentityColumn();

            entity.Property(e => e.UserId).HasMaxLength(36).IsRequired();
            entity.Property(e => e.UserDisplayName).HasMaxLength(256);
            entity.Property(e => e.UserPrincipalName).HasMaxLength(256);
            entity.Property(e => e.Availability).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Activity).HasMaxLength(50).IsRequired();
            entity.Property(e => e.DetectedAtUtc).HasColumnType("datetime2(0)").IsRequired();

            entity.HasIndex(e => new { e.UserId, e.DetectedAtUtc })
                  .HasDatabaseName("IX_UserId_DetectedAtUtc");

            entity.HasIndex(e => e.DetectedAtUtc)
                  .HasDatabaseName("IX_DetectedAtUtc")
                  .IncludeProperties(e => new { e.UserId, e.Availability });
        });
    }
}

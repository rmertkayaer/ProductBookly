using Bookly.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Bookly.Core.Persistence;

public sealed class CoreDbContext(DbContextOptions<CoreDbContext> options) : DbContext(options)
{
    /// <summary>Every Core table lives in this schema (ADR-002: schema-per-module).</summary>
    public const string Schema = "core";

    public DbSet<SkeletonCheck> SkeletonChecks => Set<SkeletonCheck>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<SkeletonCheck>(entity =>
        {
            entity.ToTable("skeleton_checks");
            entity.Property(e => e.Note).HasMaxLength(200);
        });
    }
}

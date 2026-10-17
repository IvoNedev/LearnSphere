using System.Text.Json;
using LearnSphere.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace LearnSphere.Api.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Domain.Generation> Generations => Set<Domain.Generation>();
    public DbSet<GenerationIntent> GenerationIntents => Set<GenerationIntent>();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    protected override void OnModelCreating(ModelBuilder model)
    {
        var g = model.Entity<Domain.Generation>();
        model.Entity<GenerationIntent>().HasKey(x => x.GenerationId);
        model.Entity<GenerationIntent>().HasOne<Domain.Generation>().WithOne().HasForeignKey<GenerationIntent>(x => x.GenerationId).OnDelete(DeleteBehavior.Cascade);
        g.HasKey(x => x.Id); g.Property(x => x.Status).HasConversion<string>();
        g.HasIndex(x => x.CreatedAt); g.HasIndex(x => x.Status);
        g.Property(x => x.Title).HasMaxLength(200);
        // SQLite loses DateTime.Kind; restore UTC so JSON emits Z and browser elapsed time stays correct.
        g.Property(x => x.CreatedAt).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
        g.Property(x => x.StartedAt).HasConversion(x => x, x => x.HasValue ? DateTime.SpecifyKind(x.Value, DateTimeKind.Utc) : (DateTime?)null);
        g.Property(x => x.CompletedAt).HasConversion(x => x, x => x.HasValue ? DateTime.SpecifyKind(x.Value, DateTimeKind.Utc) : (DateTime?)null);
        // Immutable value records stored as JSON TEXT; no navigation tracking is needed for results.
        g.Property(x => x.Content).HasConversion(x => JsonSerializer.Serialize(x, Json), x => JsonSerializer.Deserialize<TrainingContent>(x, Json));
        g.Property(x => x.Metadata).HasConversion(x => JsonSerializer.Serialize(x, Json), x => JsonSerializer.Deserialize<GenerationMetadata>(x, Json));
    }
}

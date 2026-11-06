using Microsoft.EntityFrameworkCore;
using Platform.Laboratories.Domain.Models;

namespace Platform.Laboratories.Infrastructure.Persistence;

public sealed class LaboratoriesDbContext(DbContextOptions<LaboratoriesDbContext> options)
    : DbContext(options)
{
    public const string SchemaName = "laboratories";

    public DbSet<LabRevision> LabRevisions => Set<LabRevision>();
    public DbSet<CoursePublishPolicy> CoursePublishPolicies => Set<CoursePublishPolicy>();
    public DbSet<RevisionDecisionRecord> RevisionDecisions => Set<RevisionDecisionRecord>();
    public DbSet<ReviewOperation> ReviewOperations => Set<ReviewOperation>();
    public DbSet<ReviewDefinition> ReviewDefinitions => Set<ReviewDefinition>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LaboratoriesDbContext).Assembly);
        var operation = modelBuilder.Entity<ReviewOperation>();
        operation.HasKey(value => new { value.RevisionId, value.ActorId, value.Key });
        operation.Property(value => value.Key).HasMaxLength(128);
        operation.Property(value => value.Action).HasMaxLength(7);
        operation.Property(value => value.Hash).HasMaxLength(64);
        operation.Property(value => value.Comment).HasMaxLength(1000);
        operation.HasIndex(value => new { value.LaboratoryId, value.Number }).IsUnique();
        operation.HasOne<LabRevision>().WithMany().HasForeignKey(value => value.RevisionId)
            .OnDelete(DeleteBehavior.Restrict);
        var definition = modelBuilder.Entity<ReviewDefinition>();
        definition.HasKey(value => value.RevisionId);
        definition.Property(value => value.RevisionId).ValueGeneratedNever();
        definition.HasOne<LabRevision>().WithOne().HasForeignKey<ReviewDefinition>(value => value.RevisionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

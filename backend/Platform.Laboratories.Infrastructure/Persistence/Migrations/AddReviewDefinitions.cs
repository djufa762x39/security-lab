using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Platform.Laboratories.Infrastructure.Persistence.Migrations;

[DbContext(typeof(LaboratoriesDbContext))]
[Migration("20261105100000_AddReviewDefinitions")]
public sealed class AddReviewDefinitions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable("ReviewDefinitions", schema: "laboratories",
            columns: table => new
            {
                RevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                LaboratoryId = table.Column<Guid>(type: "uuid", nullable: false),
                Content = table.Column<byte[]>(type: "bytea", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ReviewDefinitions", value => value.RevisionId);
                table.ForeignKey("FK_ReviewDefinitions_LabRevisions_RevisionId", value => value.RevisionId,
                    "LabRevisions", "Id", principalSchema: "laboratories", onDelete: ReferentialAction.Restrict);
            });

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable("ReviewDefinitions", schema: "laboratories");

    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
        ReviewOperationsTargetModel.Build(modelBuilder);
        AddDefinitionModel(modelBuilder);
    }

    internal static void AddDefinitionModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity("Platform.Laboratories.Infrastructure.Persistence.ReviewDefinition", b =>
        {
            b.Property<Guid>("RevisionId").HasColumnType("uuid");
            b.Property<Guid>("LaboratoryId").HasColumnType("uuid");
            b.Property<byte[]>("Content").IsRequired().HasColumnType("bytea");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.HasKey("RevisionId");
            b.ToTable("ReviewDefinitions", "laboratories");
            b.HasOne("Platform.Laboratories.Domain.Models.LabRevision", null).WithOne()
                .HasForeignKey("Platform.Laboratories.Infrastructure.Persistence.ReviewDefinition", "RevisionId")
                .OnDelete(DeleteBehavior.Restrict).IsRequired();
        });
    }
}

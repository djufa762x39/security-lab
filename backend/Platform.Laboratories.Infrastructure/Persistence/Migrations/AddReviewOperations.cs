using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Platform.Laboratories.Infrastructure.Persistence.Migrations;

[DbContext(typeof(LaboratoriesDbContext))]
[Migration("20261105090000_AddReviewOperations")]
public sealed class AddReviewOperations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("ReviewOperations", schema: "laboratories",
            columns: table => new
            {
                RevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Action = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                Hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false),
                Comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                LaboratoryId = table.Column<Guid>(type: "uuid", nullable: true),
                Number = table.Column<int>(type: "integer", nullable: true),
                Snapshot = table.Column<byte[]>(type: "bytea", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ReviewOperations", x => new { x.RevisionId, x.ActorId, x.Key });
                table.ForeignKey("FK_ReviewOperations_LabRevisions_RevisionId", x => x.RevisionId,
                    "LabRevisions", "Id", principalSchema: "laboratories", onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("IX_ReviewOperations_LaboratoryId_Number", "ReviewOperations",
            new[] { "LaboratoryId", "Number" }, schema: "laboratories", unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable("ReviewOperations", schema: "laboratories");

    protected override void BuildTargetModel(Microsoft.EntityFrameworkCore.ModelBuilder modelBuilder) =>
        ReviewOperationsTargetModel.Build(modelBuilder);
}

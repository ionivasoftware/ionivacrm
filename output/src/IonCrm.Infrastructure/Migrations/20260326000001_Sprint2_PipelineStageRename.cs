using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IonCrm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Sprint2_PipelineStageRename : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Migrate old Negotiation (4) records to Demo (3).
            // ClosedWon (5) → Musteri (5) and ClosedLost (6) → Kayip (6) need no changes
            // as integer values are preserved.
            migrationBuilder.Sql(
                "UPDATE \"Opportunities\" SET \"Stage\" = 3 WHERE \"Stage\" = 4;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No rollback needed — data migration only
        }
    }
}

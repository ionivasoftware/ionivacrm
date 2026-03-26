using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IonCrm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Sprint2_PipelineStages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Remap old Negotiation (Stage=4) records to Demo (Stage=3).
            // ClosedWon (5) → Musteri (5) and ClosedLost (6) → Kayip (6) keep same integers.
            migrationBuilder.Sql(
                "UPDATE \"Opportunities\" SET \"Stage\" = 3 WHERE \"Stage\" = 4;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE \"Opportunities\" SET \"Stage\" = 4 WHERE \"Stage\" = 3 AND \"Stage\" < 4;");
        }
    }
}

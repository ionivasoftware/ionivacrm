using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IonCrm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddApiKeysToProject : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EmsApiKey",
                table: "Projects",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RezervAlApiKey",
                table: "Projects",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmsApiKey",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "RezervAlApiKey",
                table: "Projects");
        }
    }
}

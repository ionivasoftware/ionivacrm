using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IonCrm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Sprint2_CustomerLabel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Label",
                table: "Customers",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Label",
                table: "Customers");
        }
    }
}

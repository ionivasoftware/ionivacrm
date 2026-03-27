using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IonCrm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddParasutContactIdToCustomer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ParasutContactId",
                table: "Customers",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ParasutContactId",
                table: "Customers");
        }
    }
}

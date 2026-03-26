using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IonCrm.Infrastructure.Migrations;

/// <summary>
/// Migration: Customer.Segment int? → text (project-specific free string)
///
/// CustomerStatus.Inactive (int=3) → CustomerStatus.Demo (int=3)
/// No SQL data update needed: integer value stays 3, only the C# enum name changed.
/// Existing "Inactive" rows in DB will now be interpreted as "Demo" by the application.
/// </summary>
public partial class CustomerSegmentStringAndDemoStatus : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Step 1: Drop existing int? Segment column
        migrationBuilder.DropColumn(
            name: "Segment",
            table: "Customers");

        // Step 2: Add Segment as text column
        migrationBuilder.AddColumn<string>(
            name: "Segment",
            table: "Customers",
            type: "text",
            nullable: true);

        // Note: CustomerStatus.Inactive (int=3) has been renamed to Demo (int=3).
        // No UPDATE needed — the integer value is unchanged in the database.
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Segment",
            table: "Customers");

        migrationBuilder.AddColumn<int>(
            name: "Segment",
            table: "Customers",
            type: "integer",
            nullable: true);
    }
}

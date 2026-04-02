using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IonCrm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Sprint4_NewFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ParasutConnections_ProjectId",
                table: "ParasutConnections");

            migrationBuilder.DropColumn(
                name: "Value",
                table: "Opportunities");

            migrationBuilder.AddColumn<string>(
                name: "EmsBaseUrl",
                table: "Projects",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RezervAlBaseUrl",
                table: "Projects",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SmsCount",
                table: "Projects",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<Guid>(
                name: "ProjectId",
                table: "ParasutConnections",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastConnectedAt",
                table: "ParasutConnections",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastError",
                table: "ParasutConnections",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReconnectAttempts",
                table: "ParasutConnections",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "EInvoiceAddress",
                table: "Customers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsEInvoicePayer",
                table: "Customers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "MonthlyLicenseFee",
                table: "Customers",
                type: "numeric",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Invoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    InvoiceSeries = table.Column<string>(type: "text", nullable: true),
                    InvoiceNumber = table.Column<int>(type: "integer", nullable: true),
                    IssueDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    DueDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    GrossTotal = table.Column<decimal>(type: "numeric", nullable: false),
                    NetTotal = table.Column<decimal>(type: "numeric", nullable: false),
                    LinesJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ParasutId = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Invoices_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Invoices_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ParasutProducts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductName = table.Column<string>(type: "text", nullable: false),
                    ParasutProductId = table.Column<string>(type: "text", nullable: false),
                    ParasutProductName = table.Column<string>(type: "text", nullable: true),
                    UnitPrice = table.Column<decimal>(type: "numeric", nullable: false),
                    TaxRate = table.Column<decimal>(type: "numeric", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParasutProducts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ParasutProducts_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ParasutConnections_ProjectId",
                table: "ParasutConnections",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_CustomerId",
                table: "Invoices",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_ProjectId",
                table: "Invoices",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ParasutProducts_ProjectId",
                table: "ParasutProducts",
                column: "ProjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Invoices");

            migrationBuilder.DropTable(
                name: "ParasutProducts");

            migrationBuilder.DropIndex(
                name: "IX_ParasutConnections_ProjectId",
                table: "ParasutConnections");

            migrationBuilder.DropColumn(
                name: "EmsBaseUrl",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "RezervAlBaseUrl",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "SmsCount",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "LastConnectedAt",
                table: "ParasutConnections");

            migrationBuilder.DropColumn(
                name: "LastError",
                table: "ParasutConnections");

            migrationBuilder.DropColumn(
                name: "ReconnectAttempts",
                table: "ParasutConnections");

            migrationBuilder.DropColumn(
                name: "EInvoiceAddress",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "IsEInvoicePayer",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "MonthlyLicenseFee",
                table: "Customers");

            migrationBuilder.AlterColumn<Guid>(
                name: "ProjectId",
                table: "ParasutConnections",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Value",
                table: "Opportunities",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ParasutConnections_ProjectId",
                table: "ParasutConnections",
                column: "ProjectId",
                unique: true);
        }
    }
}

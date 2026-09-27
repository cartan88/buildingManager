using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BuildingManager.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class VoidedByLeaseEnd : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Charges_LeaseId_PeriodStart",
                table: "Charges");

            migrationBuilder.AddColumn<bool>(
                name: "VoidedByLeaseEnd",
                table: "Charges",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Charges_LeaseId_PeriodStart",
                table: "Charges",
                columns: new[] { "LeaseId", "PeriodStart" },
                unique: true,
                filter: "[Type] = 'Rent' AND [PeriodStart] IS NOT NULL AND [VoidedByLeaseEnd] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Charges_LeaseId_PeriodStart",
                table: "Charges");

            migrationBuilder.DropColumn(
                name: "VoidedByLeaseEnd",
                table: "Charges");

            migrationBuilder.CreateIndex(
                name: "IX_Charges_LeaseId_PeriodStart",
                table: "Charges",
                columns: new[] { "LeaseId", "PeriodStart" },
                unique: true,
                filter: "[Type] = 'Rent' AND [PeriodStart] IS NOT NULL");
        }
    }
}

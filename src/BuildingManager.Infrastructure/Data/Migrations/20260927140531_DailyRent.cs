using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BuildingManager.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class DailyRent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "MonthlyRent",
                table: "Leases",
                newName: "Rent");

            migrationBuilder.AddColumn<string>(
                name: "Frequency",
                table: "Leases",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Monthly"); // existing leases are all monthly
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Frequency",
                table: "Leases");

            migrationBuilder.RenameColumn(
                name: "Rent",
                table: "Leases",
                newName: "MonthlyRent");
        }
    }
}

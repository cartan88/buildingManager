using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BuildingManager.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class TenantBusinessType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BusinessType",
                table: "Tenants",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BusinessType",
                table: "Tenants");
        }
    }
}

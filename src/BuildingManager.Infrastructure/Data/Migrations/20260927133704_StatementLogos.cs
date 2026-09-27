using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BuildingManager.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class StatementLogos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LogoId",
                table: "Invoices",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StatementLogos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Content = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatementLogos", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_LogoId",
                table: "Invoices",
                column: "LogoId");

            migrationBuilder.CreateIndex(
                name: "IX_StatementLogos_Sha256",
                table: "StatementLogos",
                column: "Sha256",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Invoices_StatementLogos_LogoId",
                table: "Invoices",
                column: "LogoId",
                principalTable: "StatementLogos",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Invoices_StatementLogos_LogoId",
                table: "Invoices");

            migrationBuilder.DropTable(
                name: "StatementLogos");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_LogoId",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "LogoId",
                table: "Invoices");
        }
    }
}

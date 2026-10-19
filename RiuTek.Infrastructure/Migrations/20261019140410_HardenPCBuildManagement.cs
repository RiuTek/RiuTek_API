using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RiuTek.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HardenPCBuildManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PCBuilds_UserId",
                table: "PCBuilds");

            migrationBuilder.DropIndex(
                name: "IX_PCBuildItems_PCBuildId",
                table: "PCBuildItems");

            migrationBuilder.CreateIndex(
                name: "IX_PCBuilds_UserId_CreatedAt_Id",
                table: "PCBuilds",
                columns: new[] { "UserId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_PCBuildItems_PCBuildId_ProductId",
                table: "PCBuildItems",
                columns: new[] { "PCBuildId", "ProductId" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_PCBuildItems_Quantity_Positive",
                table: "PCBuildItems",
                sql: "\"Quantity\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PCBuildItems_UnitPrice_NonNegative",
                table: "PCBuildItems",
                sql: "\"UnitPrice\" >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PCBuilds_UserId_CreatedAt_Id",
                table: "PCBuilds");

            migrationBuilder.DropIndex(
                name: "IX_PCBuildItems_PCBuildId_ProductId",
                table: "PCBuildItems");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PCBuildItems_Quantity_Positive",
                table: "PCBuildItems");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PCBuildItems_UnitPrice_NonNegative",
                table: "PCBuildItems");

            migrationBuilder.CreateIndex(
                name: "IX_PCBuilds_UserId",
                table: "PCBuilds",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PCBuildItems_PCBuildId",
                table: "PCBuildItems",
                column: "PCBuildId");
        }
    }
}

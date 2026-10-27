using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RiuTek.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProductSpecificationGinIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Products_Specifications",
                table: "Products",
                column: "Specifications")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "jsonb_path_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Products_Specifications",
                table: "Products");
        }
    }
}

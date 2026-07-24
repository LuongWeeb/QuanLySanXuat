using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WmsMes.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class EnforceUniqueBomComponents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BOMItems_BomId",
                table: "BOMItems");

            migrationBuilder.CreateIndex(
                name: "UX_BOMItems_BomId_ComponentProductId",
                table: "BOMItems",
                columns: new[] { "BomId", "ComponentProductId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_BOMItems_BomId_ComponentProductId",
                table: "BOMItems");

            migrationBuilder.CreateIndex(
                name: "IX_BOMItems_BomId",
                table: "BOMItems",
                column: "BomId");
        }
    }
}

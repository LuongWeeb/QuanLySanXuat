using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WmsMes.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class EnforceBomIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BOMs_ProductId",
                table: "BOMs");

            migrationBuilder.CreateIndex(
                name: "UX_BOMs_OneActivePerProduct",
                table: "BOMs",
                column: "ProductId",
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "UX_BOMs_ProductId_Version",
                table: "BOMs",
                columns: new[] { "ProductId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_BOMs_OneActivePerProduct",
                table: "BOMs");

            migrationBuilder.DropIndex(
                name: "UX_BOMs_ProductId_Version",
                table: "BOMs");

            migrationBuilder.CreateIndex(
                name: "IX_BOMs_ProductId",
                table: "BOMs",
                column: "ProductId");
        }
    }
}

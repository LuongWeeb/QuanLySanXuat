using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WmsMes.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueQcInspectionLot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_QCInspections_LotId",
                table: "QCInspections");

            migrationBuilder.CreateIndex(
                name: "IX_QCInspections_LotId",
                table: "QCInspections",
                column: "LotId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_QCInspections_LotId",
                table: "QCInspections");

            migrationBuilder.CreateIndex(
                name: "IX_QCInspections_LotId",
                table: "QCInspections",
                column: "LotId");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WmsMes.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationsEnhancementsFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VarianceReason",
                table: "GoodsReceiptLines",
                type: "nvarchar(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VarianceReason",
                table: "GoodsIssueLines",
                type: "nvarchar(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReasonNote",
                table: "CycleCountItems",
                type: "nvarchar(250)",
                maxLength: 250,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VarianceReason",
                table: "GoodsReceiptLines");

            migrationBuilder.DropColumn(
                name: "VarianceReason",
                table: "GoodsIssueLines");

            migrationBuilder.DropColumn(
                name: "ReasonNote",
                table: "CycleCountItems");
        }
    }
}

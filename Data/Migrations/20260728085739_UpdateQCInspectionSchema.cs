using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WmsMes.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class UpdateQCInspectionSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "WorkOrderId",
                table: "QCInspections",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<int>(
                name: "GoodsReceiptId",
                table: "QCInspections",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "QCInspections",
                type: "int",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.CreateIndex(
                name: "IX_QCInspections_GoodsReceiptId",
                table: "QCInspections",
                column: "GoodsReceiptId");

            migrationBuilder.AddForeignKey(
                name: "FK_QCInspections_GoodsReceipts_GoodsReceiptId",
                table: "QCInspections",
                column: "GoodsReceiptId",
                principalTable: "GoodsReceipts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_QCInspections_GoodsReceipts_GoodsReceiptId",
                table: "QCInspections");

            migrationBuilder.DropIndex(
                name: "IX_QCInspections_GoodsReceiptId",
                table: "QCInspections");

            migrationBuilder.DropColumn(
                name: "GoodsReceiptId",
                table: "QCInspections");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "QCInspections");

            migrationBuilder.AlterColumn<int>(
                name: "WorkOrderId",
                table: "QCInspections",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
        }
    }
}

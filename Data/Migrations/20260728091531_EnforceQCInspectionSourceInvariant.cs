using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WmsMes.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class EnforceQCInspectionSourceInvariant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE [QCInspections] SET [Type] = 1 WHERE [GoodsReceiptId] IS NOT NULL AND [WorkOrderId] IS NULL;");
            migrationBuilder.Sql(
                "UPDATE [QCInspections] SET [Type] = 2 WHERE [WorkOrderId] IS NOT NULL AND [GoodsReceiptId] IS NULL;");

            migrationBuilder.AddCheckConstraint(
                name: "CK_QCInspections_SourceMatchesType",
                table: "QCInspections",
                sql: "([Type] = 1 AND [GoodsReceiptId] IS NOT NULL AND [WorkOrderId] IS NULL) OR ([Type] = 2 AND [WorkOrderId] IS NOT NULL AND [GoodsReceiptId] IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_QCInspections_SourceMatchesType",
                table: "QCInspections");
        }
    }
}

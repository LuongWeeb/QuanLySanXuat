using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WmsMes.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class EnforceOperationsIntegrityInvariants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CycleCountItems_CycleCountOrderId",
                table: "CycleCountItems");

            migrationBuilder.AddColumn<string>(
                name: "LowStockBatchKey",
                table: "PurchaseRequests",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequests_LowStockBatchKey",
                table: "PurchaseRequests",
                column: "LowStockBatchKey",
                unique: true,
                filter: "[LowStockBatchKey] IS NOT NULL AND [Status] = 0");

            migrationBuilder.Sql(
                """
                IF EXISTS
                (
                    SELECT 1
                    FROM [CycleCountItems]
                    GROUP BY [CycleCountOrderId], [LocationId], [LotId]
                    HAVING COUNT_BIG(*) > 1
                )
                BEGIN
                    THROW 51000, 'Cannot create the unique cycle-count item index because duplicate CycleCountItems exist for (CycleCountOrderId, LocationId, LotId). Resolve duplicate CycleCountItems before retrying this migration.', 1;
                END;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_CycleCountItems_CycleCountOrderId_LocationId_LotId",
                table: "CycleCountItems",
                columns: new[] { "CycleCountOrderId", "LocationId", "LotId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PurchaseRequests_LowStockBatchKey",
                table: "PurchaseRequests");

            migrationBuilder.DropIndex(
                name: "IX_CycleCountItems_CycleCountOrderId_LocationId_LotId",
                table: "CycleCountItems");

            migrationBuilder.DropColumn(
                name: "LowStockBatchKey",
                table: "PurchaseRequests");

            migrationBuilder.CreateIndex(
                name: "IX_CycleCountItems_CycleCountOrderId",
                table: "CycleCountItems",
                column: "CycleCountOrderId");
        }
    }
}

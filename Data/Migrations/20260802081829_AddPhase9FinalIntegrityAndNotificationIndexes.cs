using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WmsMes.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPhase9FinalIntegrityAndNotificationIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PickLists_SalesOrderId",
                table: "PickLists");

            migrationBuilder.Sql(
                """
                WITH [RankedDraftPickLists] AS
                (
                    SELECT
                        [Id],
                        ROW_NUMBER() OVER (
                            PARTITION BY [SalesOrderId]
                            ORDER BY [CreatedAt], [Id]) AS [DraftRank]
                    FROM [PickLists]
                    WHERE [Status] = 0
                )
                UPDATE [PickLists]
                SET [Status] = 2
                WHERE [Id] IN
                (
                    SELECT [Id]
                    FROM [RankedDraftPickLists]
                    WHERE [DraftRank] > 1
                );
                """);

            migrationBuilder.CreateIndex(
                name: "UX_PickLists_OneDraftPerSalesOrder",
                table: "PickLists",
                column: "SalesOrderId",
                unique: true,
                filter: "[Status] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_AppNotifications_CreatedAt_Id",
                table: "AppNotifications",
                columns: new[] { "CreatedAt", "Id" },
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_AppNotifications_IsRead",
                table: "AppNotifications",
                column: "IsRead");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_PickLists_OneDraftPerSalesOrder",
                table: "PickLists");

            migrationBuilder.DropIndex(
                name: "IX_AppNotifications_CreatedAt_Id",
                table: "AppNotifications");

            migrationBuilder.DropIndex(
                name: "IX_AppNotifications_IsRead",
                table: "AppNotifications");

            migrationBuilder.CreateIndex(
                name: "IX_PickLists_SalesOrderId",
                table: "PickLists",
                column: "SalesOrderId");
        }
    }
}

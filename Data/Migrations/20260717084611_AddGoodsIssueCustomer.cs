using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WmsMes.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGoodsIssueCustomer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CustomerId",
                table: "GoodsIssues",
                type: "int",
                nullable: true);

            migrationBuilder.Sql(
                """
                -- Legacy rows predate customer ownership. A reserved inactive customer
                -- preserves them without falsely assigning a real customer. The stable
                -- unique code makes this backfill collision-safe and idempotent.
                IF EXISTS (SELECT 1 FROM [GoodsIssues] WHERE [CustomerId] IS NULL)
                   AND NOT EXISTS (SELECT 1 FROM [Customers] WHERE [Code] = N'LEGACY-UNASSIGNED')
                BEGIN
                    INSERT INTO [Customers] ([Code], [Name], [Address], [Phone], [Email], [IsActive])
                    VALUES (N'LEGACY-UNASSIGNED', N'Legacy / Unassigned Customer', N'', N'', N'', 0);
                END;

                UPDATE [GoodsIssues]
                SET [CustomerId] = (SELECT [Id] FROM [Customers] WHERE [Code] = N'LEGACY-UNASSIGNED')
                WHERE [CustomerId] IS NULL;
                """);

            migrationBuilder.AlterColumn<int>(
                name: "CustomerId",
                table: "GoodsIssues",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_GoodsIssues_CustomerId",
                table: "GoodsIssues",
                column: "CustomerId");

            migrationBuilder.AddForeignKey(
                name: "FK_GoodsIssues_Customers_CustomerId",
                table: "GoodsIssues",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GoodsIssues_Customers_CustomerId",
                table: "GoodsIssues");

            migrationBuilder.DropIndex(
                name: "IX_GoodsIssues_CustomerId",
                table: "GoodsIssues");

            migrationBuilder.DropColumn(
                name: "CustomerId",
                table: "GoodsIssues");
        }
    }
}

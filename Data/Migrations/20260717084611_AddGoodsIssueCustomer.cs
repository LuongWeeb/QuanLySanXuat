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
            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [GoodsIssues])
                    THROW 51000, 'AddGoodsIssueCustomer cannot infer customer ownership for legacy GoodsIssues. Export and archive or delete legacy GoodsIssues, or migrate them with an explicit business-approved customer mapping before retrying this migration.', 1;
                """);

            migrationBuilder.AddColumn<int>(
                name: "CustomerId",
                table: "GoodsIssues",
                type: "int",
                nullable: true);

            migrationBuilder.Sql(
                """
                IF EXISTS (SELECT 1 FROM [GoodsIssues] WHERE [CustomerId] IS NULL)
                    THROW 51001, 'AddGoodsIssueCustomer found unmapped legacy GoodsIssues. Supply an explicit customer mapping before making CustomerId required.', 1;
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

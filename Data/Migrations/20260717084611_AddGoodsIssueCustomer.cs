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
                nullable: false,
                defaultValue: 0);

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

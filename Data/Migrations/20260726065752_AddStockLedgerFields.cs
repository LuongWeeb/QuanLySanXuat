using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WmsMes.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStockLedgerFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [StockTransactions])
                    THROW 51000, N'Cannot add stock-ledger fields while historical StockTransactions exist. Reconcile historical valuation data before retrying.', 1;
                """);

            migrationBuilder.AddColumn<bool>(
                name: "IsCancelled",
                table: "StockTransactions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "QtyAfter",
                table: "StockTransactions",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ValuationRate",
                table: "StockTransactions",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsCancelled",
                table: "StockTransactions");

            migrationBuilder.DropColumn(
                name: "QtyAfter",
                table: "StockTransactions");

            migrationBuilder.DropColumn(
                name: "ValuationRate",
                table: "StockTransactions");
        }
    }
}

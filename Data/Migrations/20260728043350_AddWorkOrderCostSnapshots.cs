using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WmsMes.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkOrderCostSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ActualLaborCost",
                table: "WorkOrders",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ActualMachineCost",
                table: "WorkOrders",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ActualMaterialCost",
                table: "WorkOrders",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TargetLaborCost",
                table: "WorkOrders",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TargetMachineCost",
                table: "WorkOrders",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TargetMaterialCost",
                table: "WorkOrders",
                type: "decimal(18,2)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActualLaborCost",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "ActualMachineCost",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "ActualMaterialCost",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "TargetLaborCost",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "TargetMachineCost",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "TargetMaterialCost",
                table: "WorkOrders");
        }
    }
}

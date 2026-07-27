using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WmsMes.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCostingFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "HourlyLaborRate",
                table: "WorkCenters",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "HourlyMachineRate",
                table: "WorkCenters",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "StandardCost",
                table: "Products",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalMaterialCost",
                table: "BOMs",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalOperationCost",
                table: "BOMs",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalStandardCost",
                table: "BOMs",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HourlyLaborRate",
                table: "WorkCenters");

            migrationBuilder.DropColumn(
                name: "HourlyMachineRate",
                table: "WorkCenters");

            migrationBuilder.DropColumn(
                name: "StandardCost",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "TotalMaterialCost",
                table: "BOMs");

            migrationBuilder.DropColumn(
                name: "TotalOperationCost",
                table: "BOMs");

            migrationBuilder.DropColumn(
                name: "TotalStandardCost",
                table: "BOMs");
        }
    }
}

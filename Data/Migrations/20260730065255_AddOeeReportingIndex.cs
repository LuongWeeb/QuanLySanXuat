using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WmsMes.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOeeReportingIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkOrderSteps_WorkCenterId",
                table: "WorkOrderSteps");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderSteps_OeeReporting",
                table: "WorkOrderSteps",
                columns: new[] { "WorkCenterId", "Status", "StartTime", "EndTime" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkOrderSteps_OeeReporting",
                table: "WorkOrderSteps");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderSteps_WorkCenterId",
                table: "WorkOrderSteps",
                column: "WorkCenterId");
        }
    }
}

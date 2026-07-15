using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WmsMes.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddQcReportingPhase4 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "UnitPrice",
                table: "Lots",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitPrice",
                table: "GoodsReceiptLines",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "QCChecklists",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    StepNumber = table.Column<int>(type: "int", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QCChecklists", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QCChecklists_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QCInspections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkOrderId = table.Column<int>(type: "int", nullable: false),
                    LotId = table.Column<int>(type: "int", nullable: false),
                    InspectionTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    InspectorId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Result = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    EvidencePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QCInspections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QCInspections_Lots_LotId",
                        column: x => x.LotId,
                        principalTable: "Lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QCInspections_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QCChecklistItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    QCChecklistId = table.Column<int>(type: "int", nullable: false),
                    ParameterName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    MinVal = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    MaxVal = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QCChecklistItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QCChecklistItems_QCChecklists_QCChecklistId",
                        column: x => x.QCChecklistId,
                        principalTable: "QCChecklists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QCInspectionLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    QCInspectionId = table.Column<int>(type: "int", nullable: false),
                    ParameterName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    ValueInspected = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    IsOK = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QCInspectionLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QCInspectionLines_QCInspections_QCInspectionId",
                        column: x => x.QCInspectionId,
                        principalTable: "QCInspections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QCChecklistItems_QCChecklistId",
                table: "QCChecklistItems",
                column: "QCChecklistId");

            migrationBuilder.CreateIndex(
                name: "IX_QCChecklists_ProductId",
                table: "QCChecklists",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_QCInspectionLines_QCInspectionId",
                table: "QCInspectionLines",
                column: "QCInspectionId");

            migrationBuilder.CreateIndex(
                name: "IX_QCInspections_LotId",
                table: "QCInspections",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_QCInspections_WorkOrderId",
                table: "QCInspections",
                column: "WorkOrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QCChecklistItems");

            migrationBuilder.DropTable(
                name: "QCInspectionLines");

            migrationBuilder.DropTable(
                name: "QCChecklists");

            migrationBuilder.DropTable(
                name: "QCInspections");

            migrationBuilder.DropColumn(
                name: "UnitPrice",
                table: "Lots");

            migrationBuilder.DropColumn(
                name: "UnitPrice",
                table: "GoodsReceiptLines");
        }
    }
}

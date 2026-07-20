using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using WmsMes.Web.Controllers;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Services;

namespace WmsMes.Tests;

public class ReportExportTests
{
    private static DbContextOptions<ApplicationDbContext> Options(string name) =>
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(name).Options;

    [Fact]
    public async Task ExportStockBalanceToExcel_ReturnsNonEmptyByteArray()
    {
        await using var context = new ApplicationDbContext(Options($"Report_Excel_{Guid.NewGuid()}"));
        var warehouse = new Warehouse { Code = "WH-01", Name = "Kho chính" };
        var location = new Location
        {
            Code = "A-01",
            Name = "Kệ A-01",
            Zone = new Zone { Code = "Z-01", Name = "Khu A", Warehouse = warehouse }
        };
        var product = new Product
        {
            Code = "SP-001",
            Name = "Sản phẩm kiểm thử",
            BaseUom = new UnitOfMeasure { Code = "KG", Name = "Kilogram" }
        };
        context.StockBalances.Add(new StockBalance
        {
            Product = product,
            Lot = new Lot { LotNo = "LOT-001", Product = product, ExpiryDate = new DateTime(2027, 1, 31) },
            Location = location,
            QtyAvailable = 1234.5m
        });
        await context.SaveChangesAsync();

        var bytes = await new ReportExportService(context).ExportStockBalanceToExcelAsync(warehouse.Id);

        Assert.True(bytes.Length > 1000);
        Assert.Equal("PK", System.Text.Encoding.ASCII.GetString(bytes, 0, 2));
    }

    [Fact]
    public async Task ExportWorkOrderToPdf_ReturnsPdfDocument()
    {
        await using var context = new ApplicationDbContext(Options($"Report_Pdf_{Guid.NewGuid()}"));
        var product = new Product { Code = "FG-001", Name = "Thành phẩm" };
        var order = new WorkOrder
        {
            Code = "WO-001",
            Product = product,
            Qty = 250,
            DueDate = new DateTime(2026, 8, 1),
            BomVersion = "B1",
            RoutingVersion = "R1"
        };
        order.Steps.Add(new WorkOrderStep
        {
            StepNumber = 1,
            StepName = "Phối trộn",
            WorkCenter = new WorkCenter { Code = "MIX", Name = "Máy trộn" }
        });
        context.WorkOrders.Add(order);
        await context.SaveChangesAsync();

        var bytes = await new ReportExportService(context).ExportWorkOrderToPdfAsync(order.Id);

        Assert.True(bytes.Length > 1000);
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));
    }

    [Fact]
    public async Task InventoryExportExcel_ReturnsExcelFileFromReportService()
    {
        await using var context = new ApplicationDbContext(Options($"Report_InventoryController_{Guid.NewGuid()}"));
        var expected = new byte[] { 1, 2, 3 };
        var reports = new Mock<IReportExportService>();
        reports.Setup(x => x.ExportStockBalanceToExcelAsync(17)).ReturnsAsync(expected);
        var controller = new InventoryController(context, reportExportService: reports.Object);

        var result = await controller.ExportExcel(17);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Same(expected, file.FileContents);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", file.ContentType);
        Assert.StartsWith("TonKho_", file.FileDownloadName);
        Assert.EndsWith(".xlsx", file.FileDownloadName);
        reports.VerifyAll();
    }

    [Fact]
    public async Task WorkOrderExportPdf_ReturnsPdfFileFromReportService()
    {
        await using var context = new ApplicationDbContext(Options($"Report_WorkOrderController_{Guid.NewGuid()}"));
        var expected = new byte[] { 4, 5, 6 };
        var reports = new Mock<IReportExportService>();
        reports.Setup(x => x.ExportWorkOrderToPdfAsync(23)).ReturnsAsync(expected);
        var controller = new WorkOrderController(
            context,
            Mock.Of<IWorkOrderService>(),
            Mock.Of<ILogger<WorkOrderController>>(),
            reports.Object);

        var result = await controller.ExportPdf(23);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Same(expected, file.FileContents);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.StartsWith("LenhSanXuat_23_", file.FileDownloadName);
        Assert.EndsWith(".pdf", file.FileDownloadName);
        reports.VerifyAll();
    }
}

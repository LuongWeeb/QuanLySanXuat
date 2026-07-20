using ClosedXML.Excel;
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
    public async Task ExportStockBalanceToExcel_ReturnsRequestedFormattedWorkbook()
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
        var excludedProduct = new Product
        {
            Code = "SP-EXCLUDED",
            Name = "Sản phẩm kho khác",
            BaseUom = new UnitOfMeasure { Code = "EA", Name = "Cái" }
        };
        context.StockBalances.Add(new StockBalance
        {
            Product = excludedProduct,
            Lot = new Lot { LotNo = "LOT-EXCLUDED", Product = excludedProduct },
            Location = new Location
            {
                Code = "B-01",
                Name = "Kệ B-01",
                Zone = new Zone
                {
                    Code = "Z-02",
                    Name = "Khu B",
                    Warehouse = new Warehouse { Code = "WH-02", Name = "Kho phụ" }
                }
            },
            QtyAvailable = 99m
        });
        await context.SaveChangesAsync();

        var bytes = await new ReportExportService(context).ExportStockBalanceToExcelAsync(warehouse.Id);

        Assert.True(bytes.Length > 1000);
        Assert.Equal("PK", System.Text.Encoding.ASCII.GetString(bytes, 0, 2));

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var worksheet = workbook.Worksheet("Tồn kho");
        Assert.Equal(
            new[] { "Mã SP", "Tên SP", "Lô", "Vị trí", "Số lượng khả dụng", "Đơn vị tính", "Hạn dùng" },
            worksheet.Row(1).Cells(1, 7).Select(cell => cell.GetString()));
        Assert.Equal(2, worksheet.LastRowUsed()!.RowNumber());
        Assert.Equal("SP-001", worksheet.Cell(2, 1).GetString());
        Assert.Equal("Sản phẩm kiểm thử", worksheet.Cell(2, 2).GetString());
        Assert.Equal("LOT-001", worksheet.Cell(2, 3).GetString());
        Assert.Equal("A-01", worksheet.Cell(2, 4).GetString());
        Assert.Equal(1234.5m, worksheet.Cell(2, 5).GetValue<decimal>());
        Assert.Equal("KG", worksheet.Cell(2, 6).GetString());
        Assert.Equal(new DateTime(2027, 1, 31), worksheet.Cell(2, 7).GetDateTime());
        Assert.True(worksheet.Cell(1, 1).Style.Font.Bold);
        Assert.Equal(XLColor.White.Color.ToArgb(), worksheet.Cell(1, 1).Style.Font.FontColor.Color.ToArgb());
        Assert.Equal(XLColor.FromHtml("#1E293B").Color.ToArgb(), worksheet.Cell(1, 1).Style.Fill.BackgroundColor.Color.ToArgb());
        Assert.Equal("#,##0.00", worksheet.Cell(2, 5).Style.NumberFormat.Format);
        Assert.Equal("dd/MM/yyyy", worksheet.Cell(2, 7).Style.DateFormat.Format);
    }

    [Fact]
    public async Task ExportWorkOrderToPdf_ReturnsPdfDocument()
    {
        await using var context = new ApplicationDbContext(Options($"Report_Pdf_{Guid.NewGuid()}"));
        var product = new Product { Code = "FG-001", Name = "Thành phẩm" };
        var order = new WorkOrder
        {
            Code = "WO_001",
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
        var hyphenOrder = new WorkOrder
        {
            Code = "WO-001",
            Product = product,
            Qty = 250,
            DueDate = new DateTime(2026, 8, 1),
            BomVersion = "B1",
            RoutingVersion = "R1"
        };
        context.WorkOrders.AddRange(order, hyphenOrder);
        await context.SaveChangesAsync();

        var bytes = await new ReportExportService(context).ExportWorkOrderToPdfAsync(order.Id);
        var hyphenBytes = await new ReportExportService(context).ExportWorkOrderToPdfAsync(hyphenOrder.Id);

        Assert.True(bytes.Length > 1000);
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));
        Assert.Contains("/Subtype /Image", System.Text.Encoding.Latin1.GetString(bytes));
        Assert.NotEqual(
            Convert.ToHexString(FirstPdfImageStream(hyphenBytes)),
            Convert.ToHexString(FirstPdfImageStream(bytes)));
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

    private static byte[] FirstPdfImageStream(byte[] pdf)
    {
        var imageMarker = System.Text.Encoding.ASCII.GetBytes("/Subtype /Image");
        var imageIndex = pdf.AsSpan().IndexOf(imageMarker);
        Assert.True(imageIndex >= 0, "The PDF must contain an embedded image.");

        var streamMarker = System.Text.Encoding.ASCII.GetBytes("stream");
        var relativeStreamIndex = pdf.AsSpan(imageIndex).IndexOf(streamMarker);
        Assert.True(relativeStreamIndex >= 0, "The embedded image must contain a PDF stream.");
        var streamStart = imageIndex + relativeStreamIndex + streamMarker.Length;
        while (streamStart < pdf.Length && (pdf[streamStart] == (byte)'\r' || pdf[streamStart] == (byte)'\n'))
        {
            streamStart++;
        }

        var endMarker = System.Text.Encoding.ASCII.GetBytes("endstream");
        var relativeEndIndex = pdf.AsSpan(streamStart).IndexOf(endMarker);
        Assert.True(relativeEndIndex >= 0, "The embedded image stream must be terminated.");
        return pdf.AsSpan(streamStart, relativeEndIndex).ToArray();
    }
}

using ClosedXML.Excel;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using SkiaSharp;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using WmsMes.Web.Controllers;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Domain.Enums;
using WmsMes.Web.Services;
using ZXing;
using ZXing.Common;

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
    public async Task ExportWorkOrderToPdf_PreservesVietnameseCodeQuantityAndRequestedOperationOrder()
    {
        await using var context = new ApplicationDbContext(Options($"Report_Pdf_{Guid.NewGuid()}"));
        var product = new Product { Code = "FG-001", Name = "Thành phẩm" };
        var order = new WorkOrder
        {
            Code = "LỆNH-SX-ĐẶC-BIỆT-001",
            Product = product,
            Qty = 250,
            DueDate = new DateTime(2026, 8, 1),
            BomVersion = "B1",
            RoutingVersion = "R1"
        };
        WorkOrderStep[] requestedSteps =
        [
            new WorkOrderStep
            {
                StepNumber = 30,
                StepName = "OP-30 FINAL PACKING",
                Status = WorkOrderStepStatus.Completed,
                WorkCenter = new WorkCenter { Code = "PACK", Name = "Đóng gói" }
            },
            new WorkOrderStep
            {
                StepNumber = 10,
                StepName = "OP-10 MATERIAL PREPARATION",
                Status = WorkOrderStepStatus.InProgress,
                WorkCenter = new WorkCenter { Code = "PREP", Name = "Chuẩn bị" }
            },
            new WorkOrderStep
            {
                StepNumber = 20,
                StepName = "OP-20 PRIMARY MIXING",
                Status = WorkOrderStepStatus.Pending,
                WorkCenter = new WorkCenter { Code = "MIX", Name = "Máy trộn" }
            }
        ];
        foreach (var step in requestedSteps)
        {
            order.Steps.Add(step);
        }
        context.WorkOrders.Add(order);
        await context.SaveChangesAsync();

        var originalCulture = CultureInfo.CurrentCulture;
        byte[] bytes;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            bytes = await new ReportExportService(context).ExportWorkOrderToPdfAsync(order.Id);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }

        Assert.True(bytes.Length > 1000);
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));

        using var document = PdfDocument.Open(bytes);
        var page = document.GetPage(1);
        var text = Regex.Replace(ContentOrderTextExtractor.GetText(page), @"\s+", " ");
        Assert.Contains("250,00", text);
        var preparationIndex = text.IndexOf("OP-10 MATERIAL PREPARATION", StringComparison.Ordinal);
        var mixingIndex = text.IndexOf("OP-20 PRIMARY MIXING", StringComparison.Ordinal);
        var packingIndex = text.IndexOf("OP-30 FINAL PACKING", StringComparison.Ordinal);
        Assert.True(preparationIndex >= 0, $"Missing exact first operation in PDF text: {text}");
        Assert.True(mixingIndex > preparationIndex, $"Second operation is not after the first: {text}");
        Assert.True(packingIndex > mixingIndex, $"Third operation is not after the second: {text}");

        var decodedQr = page.GetImages()
            .Select(TryDecodeQr)
            .FirstOrDefault(value => value is not null);
        Assert.Equal(order.Code, decodedQr);
    }

    [Fact]
    public async Task InventoryExportExcel_ReturnsExcelFileFromReportService()
    {
        await using var context = new ApplicationDbContext(Options($"Report_InventoryController_{Guid.NewGuid()}"));
        var expected = new byte[] { 1, 2, 3 };
        var reports = new Mock<IReportExportService>();
        reports.Setup(x => x.ExportStockBalanceToExcelAsync(17)).ReturnsAsync(expected);
        var controller = new InventoryController(context, reports.Object);

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
            reports.Object,
            TimeProvider.System,
            TimeZoneInfo.Utc);

        var result = await controller.ExportPdf(23);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Same(expected, file.FileContents);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.StartsWith("LenhSanXuat_23_", file.FileDownloadName);
        Assert.EndsWith(".pdf", file.FileDownloadName);
        reports.VerifyAll();
    }

    [Fact]
    public async Task WorkOrderExportPdf_WhenReportDoesNotExist_ReturnsNotFound()
    {
        await using var context = new ApplicationDbContext(Options($"Report_WorkOrderMissing_{Guid.NewGuid()}"));
        var reports = new Mock<IReportExportService>();
        reports.Setup(x => x.ExportWorkOrderToPdfAsync(404))
            .ThrowsAsync(new KeyNotFoundException("missing"));
        var controller = new WorkOrderController(
            context,
            Mock.Of<IWorkOrderService>(),
            Mock.Of<ILogger<WorkOrderController>>(),
            reports.Object,
            TimeProvider.System,
            TimeZoneInfo.Utc);

        var result = await controller.ExportPdf(404);

        Assert.IsType<NotFoundResult>(result);
    }

    [Theory]
    [InlineData(typeof(InventoryController))]
    [InlineData(typeof(WorkOrderController))]
    public void ReportExportService_IsARequiredNonNullableControllerDependency(Type controllerType)
    {
        var constructor = Assert.Single(controllerType.GetConstructors());
        var parameter = Assert.Single(constructor.GetParameters().Where(item => item.ParameterType == typeof(IReportExportService)));

        Assert.False(parameter.HasDefaultValue);
        Assert.Equal(NullabilityState.NotNull, new NullabilityInfoContext().Create(parameter).ReadState);
    }

    private static string? TryDecodeQr(UglyToad.PdfPig.Content.IPdfImage image)
    {
        var imageBytes = image.TryGetPng(out var pngBytes)
            ? pngBytes
            : image.RawBytes.ToArray();
        using var bitmap = SKBitmap.Decode(imageBytes);
        if (bitmap is null)
        {
            return null;
        }

        var rgb = new byte[bitmap.Width * bitmap.Height * 3];
        var offset = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                rgb[offset++] = pixel.Red;
                rgb[offset++] = pixel.Green;
                rgb[offset++] = pixel.Blue;
            }
        }

        var source = new RGBLuminanceSource(
            rgb,
            bitmap.Width,
            bitmap.Height,
            RGBLuminanceSource.BitmapFormat.RGB24);
        var reader = new BarcodeReaderGeneric
        {
            Options = new DecodingOptions
            {
                CharacterSet = "UTF-8",
                PossibleFormats = [BarcodeFormat.QR_CODE],
                TryHarder = true
            }
        };
        return reader.Decode(source)?.Text;
    }
}

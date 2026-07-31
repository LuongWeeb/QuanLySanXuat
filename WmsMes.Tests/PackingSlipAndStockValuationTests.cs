using System.Reflection;
using System.Text;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;
using UglyToad.PdfPig;
using WmsMes.Web.Controllers;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.ViewModels;
using ZXing;
using ZXing.Common;

namespace WmsMes.Tests;

public class PackingSlipAndStockValuationTests : IClassFixture<PdfFontRegistrationFixture>
{
    public PackingSlipAndStockValuationTests(PdfFontRegistrationFixture _)
    {
    }

    [Fact]
    public async Task PrintPackingSlip_ReturnsNamed100MillimetrePdfWithPackageQrAndOrderDetails()
    {
        var options = CreateOptions();
        await using (var context = new ApplicationDbContext(options))
        {
            var product = new Product
            {
                Id = 1,
                Code = "SKU-PACK-01",
                Name = "San pham dong goi",
                BaseUomId = 1
            };
            var salesOrder = new SalesOrder
            {
                Id = 2,
                OrderNo = "SO-20260731-001",
                Customer = new Customer { Id = 3, Code = "CUS-01", Name = "Khach hang A" },
                DeliveryDate = new DateTime(2026, 8, 1),
                Items =
                [
                    new SalesOrderItem { Id = 4, Product = product, Qty = 12m, UnitPrice = 25_000m }
                ]
            };
            context.PackingSlips.Add(new PackingSlip
            {
                Id = 5,
                PackingNo = "PS-20260731-001",
                SalesOrder = salesOrder,
                PackageNo = 1,
                GrossWeight = 18.5m
            });
            context.PackingSlips.Add(new PackingSlip
            {
                Id = 6,
                PackingNo = "PS-20260731-003",
                SalesOrderId = 2,
                PackageNo = 3
            });
            await context.SaveChangesAsync();
        }

        await using var assertionContext = new ApplicationDbContext(options);
        var result = await new PrintController(assertionContext).PrintPackingSlip(5);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal("PhieuDongGoi_PS-20260731-001.pdf", file.FileDownloadName);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(file.FileContents, 0, 5));
        using var pdf = PdfDocument.Open(file.FileContents);
        var page = Assert.Single(pdf.GetPages());
        Assert.InRange(page.Width, 283.4, 283.6);
        Assert.InRange(page.Height, 283.4, 283.6);
        var text = page.Text;
        Assert.Contains("PS-20260731-001", text);
        Assert.Contains("SO-20260731-001", text);
        Assert.Contains("Khach hang A", text);
        Assert.Contains("Thùng 1 / 3", text);
        Assert.Contains("SKU-PACK-01", text);
        Assert.Equal("PS-20260731-001", DecodeQr(page));
    }

    [Fact]
    public async Task PrintPackingSlip_ReturnsNotFoundWhenPackingSlipDoesNotExist()
    {
        await using var context = new ApplicationDbContext(CreateOptions());

        var result = await new PrintController(context).PrintPackingSlip(404);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task PrintPackingSlip_StaysOnOnePageAndSignalsTruncatedProductsForLongPackages()
    {
        var options = CreateOptions();
        await using (var context = new ApplicationDbContext(options))
        {
            var salesOrder = new SalesOrder
            {
                Id = 30,
                OrderNo = new string('O', 50),
                Customer = new Customer { Id = 31, Code = "CUS-LONG", Name = new string('C', 250) },
                DeliveryDate = new DateTime(2026, 8, 1),
                Items = Enumerable.Range(1, 24).Select(index => new SalesOrderItem
                {
                    Id = 100 + index,
                    Product = new Product
                    {
                        Id = 200 + index,
                        Code = $"SKU-LONG-{index:D2}",
                        Name = new string('P', 250),
                        BaseUomId = 1
                    },
                    Qty = index,
                    UnitPrice = 1m
                }).ToList()
            };
            context.PackingSlips.Add(new PackingSlip
            {
                Id = 32,
                PackingNo = "PS-LONG-001",
                SalesOrder = salesOrder,
                PackageNo = 1
            });
            await context.SaveChangesAsync();
        }

        await using var assertionContext = new ApplicationDbContext(options);
        var result = await new PrintController(assertionContext).PrintPackingSlip(32);

        var file = Assert.IsType<FileContentResult>(result);
        using var pdf = PdfDocument.Open(file.FileContents);
        var page = Assert.Single(pdf.GetPages());
        Assert.InRange(page.Width, 283.4, 283.6);
        Assert.InRange(page.Height, 283.4, 283.6);
        var text = page.Text;
        Assert.Contains("PHIẾU ĐÓNG GÓI", text);
        Assert.Contains("PS-LONG-001", text);
        Assert.Contains("SKU-LONG-01", text);
        Assert.Contains("Còn 18 sản phẩm", text);
        Assert.Equal("PS-LONG-001", DecodeQr(page));
    }

    [Fact]
    public async Task StockValuation_ReturnsOnlyAvailableBalancesInDeterministicOrder()
    {
        var options = CreateOptions();
        await SeedStockBalancesAsync(options);

        await using var context = new ApplicationDbContext(options);
        var result = await new ReportController(context).StockValuation();

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<StockValuationViewModel>(view.Model);
        Assert.Equal(["SKU-A", "SKU-B"], model.Balances.Select(balance => balance.Product!.Code));
    }

    [Fact]
    public async Task ExportStockValuationExcel_ReturnsFormattedWorkbookWithComputedLineAndGrandTotals()
    {
        var options = CreateOptions();
        await SeedStockBalancesAsync(options);

        await using var context = new ApplicationDbContext(options);
        var result = await new ReportController(context).ExportStockValuationExcel();

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", file.ContentType);
        Assert.StartsWith("BaoCao_TaiChinh_Kho_", file.FileDownloadName);
        Assert.EndsWith(".xlsx", file.FileDownloadName);
        Assert.Equal("PK", Encoding.ASCII.GetString(file.FileContents, 0, 2));

        using var workbook = new XLWorkbook(new MemoryStream(file.FileContents));
        var worksheet = workbook.Worksheet("Báo cáo Tài chính Kho");
        Assert.Equal("BÁO CÁO GIÁ TRỊ TỒN KHO & TÀI CHÍNH", worksheet.Cell(1, 1).GetString());
        Assert.Equal(
            ["Mã SKU", "Tên sản phẩm", "Tên kho", "Vị trí", "Số lô", "Số lượng tồn", "Đơn giá vốn (VNĐ)", "Tổng giá trị (VNĐ)"],
            worksheet.Row(3).Cells(1, 8).Select(cell => cell.GetString()));
        Assert.Equal("SKU-A", worksheet.Cell(4, 1).GetString());
        Assert.Equal("SKU-B", worksheet.Cell(5, 1).GetString());
        Assert.Equal(12.5m, worksheet.Cell(4, 6).GetValue<decimal>());
        Assert.Equal(12_345.67m, worksheet.Cell(4, 7).GetValue<decimal>());
        Assert.Equal(154_320.875m, worksheet.Cell(4, 8).GetValue<decimal>());
        Assert.Equal(20m, worksheet.Cell(5, 6).GetValue<decimal>());
        Assert.Equal(50m, worksheet.Cell(5, 7).GetValue<decimal>());
        Assert.Equal(1_000m, worksheet.Cell(5, 8).GetValue<decimal>());
        Assert.Equal("TỔNG CỘNG", worksheet.Cell(6, 7).GetString());
        Assert.Equal(155_320.875m, worksheet.Cell(6, 8).GetValue<decimal>());
        Assert.Contains("#,##0", worksheet.Cell(4, 6).Style.NumberFormat.Format);
        Assert.Contains("#,##0", worksheet.Cell(4, 7).Style.NumberFormat.Format);
        Assert.Contains("#,##0", worksheet.Cell(4, 8).Style.NumberFormat.Format);
        Assert.Equal(
            XLColor.FromHtml("#0D6EFD").Color.ToArgb(),
            worksheet.Range("A3:H3").FirstCell().Style.Fill.BackgroundColor.Color.ToArgb());
    }

    [Fact]
    public async Task ExportStockValuationExcel_ProducesValidStandardFontWorkbookWhenNoBalanceIsAvailable()
    {
        await using var context = new ApplicationDbContext(CreateOptions());

        var result = await new ReportController(context).ExportStockValuationExcel();

        var file = Assert.IsType<FileContentResult>(result);
        using var workbook = new XLWorkbook(new MemoryStream(file.FileContents));
        var worksheet = workbook.Worksheet("Báo cáo Tài chính Kho");
        Assert.NotEqual("#,##0.00", worksheet.Cell(4, 6).Style.NumberFormat.Format);
        Assert.Equal("Arial", worksheet.Style.Font.FontName);
        Assert.Equal("TỔNG CỘNG", worksheet.Cell(4, 7).GetString());
        Assert.Equal(0m, worksheet.Cell(4, 8).GetValue<decimal>());
        Assert.Equal("#,##0.00", worksheet.Cell(4, 8).Style.NumberFormat.Format);
    }

    [Fact]
    public void ReportController_RequiresAuthenticatedUser()
    {
        Assert.NotNull(typeof(ReportController).GetCustomAttribute<AuthorizeAttribute>());
    }

    private static async Task SeedStockBalancesAsync(DbContextOptions<ApplicationDbContext> options)
    {
        await using var context = new ApplicationDbContext(options);
        var warehouse = new Warehouse { Id = 10, Code = "WH-01", Name = "Kho chinh" };
        var zone = new Zone { Id = 11, Code = "Z-01", Name = "Khu A", Warehouse = warehouse };
        var locationB = new Location { Id = 12, Code = "B-01", Name = "Ke B", Zone = zone };
        var locationA = new Location { Id = 13, Code = "A-01", Name = "Ke A", Zone = zone };
        var productB = new Product { Id = 14, Code = "SKU-B", Name = "San pham B", BaseUomId = 1 };
        var productA = new Product { Id = 15, Code = "SKU-A", Name = "San pham A", BaseUomId = 1 };
        context.StockBalances.AddRange(
            new StockBalance
            {
                Id = 16,
                Product = productB,
                Lot = new Lot { Id = 17, LotNo = "LOT-B", Product = productB, UnitPrice = 50m },
                Location = locationB,
                QtyAvailable = 20m
            },
            new StockBalance
            {
                Id = 18,
                Product = productA,
                Lot = new Lot { Id = 19, LotNo = "LOT-A", Product = productA, UnitPrice = 12_345.67m },
                Location = locationA,
                QtyAvailable = 12.5m
            },
            new StockBalance
            {
                Id = 20,
                Product = new Product { Id = 21, Code = "SKU-ZERO", Name = "Khong ton", BaseUomId = 1 },
                Lot = new Lot { Id = 22, LotNo = "LOT-ZERO", ProductId = 21, UnitPrice = 99m },
                Location = locationA,
                QtyAvailable = 0m
            });
        await context.SaveChangesAsync();
    }

    private static DbContextOptions<ApplicationDbContext> CreateOptions()
    {
        return new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
    }

    private static string? DecodeQr(UglyToad.PdfPig.Content.Page page)
    {
        var image = page.GetImages().FirstOrDefault();
        Assert.NotNull(image);
        var imageBytes = image!.TryGetPng(out var pngBytes) ? pngBytes : image.RawBytes.ToArray();
        using var bitmap = SKBitmap.Decode(imageBytes);
        Assert.NotNull(bitmap);
        var rgb = new byte[bitmap!.Width * bitmap.Height * 3];
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

        return new BarcodeReaderGeneric
        {
            Options = new DecodingOptions
            {
                CharacterSet = "UTF-8",
                PossibleFormats = [BarcodeFormat.QR_CODE],
                TryHarder = true
            }
        }.Decode(new RGBLuminanceSource(rgb, bitmap.Width, bitmap.Height, RGBLuminanceSource.BitmapFormat.RGB24))?.Text;
    }
}

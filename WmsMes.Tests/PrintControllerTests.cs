using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;
using System.Text;
using UglyToad.PdfPig;
using WmsMes.Web.Controllers;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using ZXing;
using ZXing.Common;

namespace WmsMes.Tests;

public class PrintControllerTests
{
    [Fact]
    public async Task PrintLocation_ReturnsNonEmptyPdf_WhenLocationExists()
    {
        var options = CreateOptions();
        await using (var context = new ApplicationDbContext(options))
        {
            var warehouse = new Warehouse { Code = "WH01", Name = "Kho A" };
            var zone = new Zone { Code = "ZONE-01", Name = "Khu 1", Warehouse = warehouse };
            context.Locations.Add(new Location
            {
                Id = 10,
                Code = "LOC-10",
                Name = "Vị trí 10",
                Zone = zone
            });
            await context.SaveChangesAsync();
        }

        await using var assertionContext = new ApplicationDbContext(options);
        var controller = new PrintController(assertionContext);

        var result = await controller.PrintLocation(10);

        var fileResult = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/pdf", fileResult.ContentType);
        Assert.True(string.IsNullOrEmpty(fileResult.FileDownloadName));
        Assert.NotEmpty(fileResult.FileContents);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(fileResult.FileContents, 0, 5));
        AssertLabelPdf(fileResult.FileContents, "LOC-10");
    }

    [Fact]
    public async Task PrintLot_ReturnsNonEmptyPdf_WhenLotExists()
    {
        var options = CreateOptions();
        await using (var context = new ApplicationDbContext(options))
        {
            var product = new Product
            {
                Code = "RM-FRAME-01",
                Name = "Khung xe",
                BaseUomId = 1,
                IsLotTracked = true
            };
            context.Lots.Add(new Lot
            {
                Id = 20,
                LotNo = "LOT-100",
                Product = product,
                ManufactureDate = new DateTime(2026, 7, 1),
                ExpiryDate = new DateTime(2027, 7, 1),
                Qty = 10
            });
            await context.SaveChangesAsync();
        }

        await using var assertionContext = new ApplicationDbContext(options);
        var controller = new PrintController(assertionContext);

        var result = await controller.PrintLot(20);

        var fileResult = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/pdf", fileResult.ContentType);
        Assert.True(string.IsNullOrEmpty(fileResult.FileDownloadName));
        Assert.NotEmpty(fileResult.FileContents);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(fileResult.FileContents, 0, 5));
        AssertLabelPdf(fileResult.FileContents, "LOT-100");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PrintLabel_ReturnsNotFound_WhenEntityDoesNotExist(bool locationLabel)
    {
        await using var context = new ApplicationDbContext(CreateOptions());
        var controller = new PrintController(context);

        var result = locationLabel
            ? await controller.PrintLocation(404)
            : await controller.PrintLot(404);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task PrintLabels_StayOnOneStandardPage_WithMaximumLengthText()
    {
        var options = CreateOptions();
        await using (var context = new ApplicationDbContext(options))
        {
            var warehouse = new Warehouse { Code = new string('W', 50), Name = new string('K', 150) };
            var zone = new Zone { Code = new string('Z', 50), Name = new string('N', 150), Warehouse = warehouse };
            context.Locations.Add(new Location
            {
                Id = 30,
                Code = new string('L', 50),
                Name = new string('V', 150),
                Zone = zone
            });
            context.Lots.Add(new Lot
            {
                Id = 40,
                LotNo = new string('T', 100),
                Product = new Product
                {
                    Code = new string('P', 100),
                    Name = new string('S', 250),
                    BaseUomId = 1
                }
            });
            await context.SaveChangesAsync();
        }

        await using var assertionContext = new ApplicationDbContext(options);
        var controller = new PrintController(assertionContext);
        var location = Assert.IsType<FileContentResult>(await controller.PrintLocation(30));
        var lot = Assert.IsType<FileContentResult>(await controller.PrintLot(40));

        AssertLabelPdf(location.FileContents, new string('L', 50));
        AssertLabelPdf(lot.FileContents, new string('T', 100));
    }

    private static DbContextOptions<ApplicationDbContext> CreateOptions()
    {
        return new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
    }

    private static void AssertLabelPdf(byte[] bytes, string expectedQrPayload)
    {
        using var document = PdfDocument.Open(bytes);
        var page = Assert.Single(document.GetPages());
        Assert.InRange(page.Width, 283.4, 283.6);
        Assert.InRange(page.Height, 141.6, 141.9);
        var decodedQr = page.GetImages()
            .Select(TryDecodeQr)
            .FirstOrDefault(value => value is not null);
        Assert.Equal(expectedQrPayload, decodedQr);
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

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;
using WmsMes.Web.Controllers;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;

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
        Assert.Equal("Label_Loc_LOC-10.pdf", fileResult.FileDownloadName);
        Assert.NotEmpty(fileResult.FileContents);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(fileResult.FileContents, 0, 5));
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
        Assert.Equal("Label_Lot_LOT-100.pdf", fileResult.FileDownloadName);
        Assert.NotEmpty(fileResult.FileContents);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(fileResult.FileContents, 0, 5));
    }

    private static DbContextOptions<ApplicationDbContext> CreateOptions()
    {
        return new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
    }
}

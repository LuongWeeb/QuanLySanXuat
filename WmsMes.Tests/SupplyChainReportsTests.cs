using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Services;
using System.Net;

namespace WmsMes.Tests;

public class SupplyChainReportsTests : IClassFixture<InventoryCancellationWebApplicationFactory>
{
    private readonly InventoryCancellationWebApplicationFactory _factory;

    public SupplyChainReportsTests(InventoryCancellationWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PickListSequenceOrdering_OrdersWarehouseLocationsByZoneThenLocationCode()
    {
        var options = CreateOptions();
        await using (var context = new ApplicationDbContext(options))
        {
            var warehouse = new Warehouse { Id = 1, Code = "WH-MAIN", Name = "Main warehouse" };
            var zoneA = new Zone { Id = 2, Warehouse = warehouse, Code = "ZONE-A", Name = "Aisle A" };
            var zoneB = new Zone { Id = 3, Warehouse = warehouse, Code = "ZONE-B", Name = "Aisle B" };
            var product = new Product { Id = 4, Code = "SKU-PICK", Name = "Pick product", BaseUomId = 1 };

            context.Customers.Add(new Customer { Id = 5, Code = "CUS-PICK", Name = "Pick customer" });
            context.SalesOrders.Add(new SalesOrder
            {
                Id = 6,
                OrderNo = "SO-PICK-001",
                CustomerId = 5,
                DeliveryDate = new DateTime(2026, 8, 1),
                Items = [new SalesOrderItem { Product = product, Qty = 9m }]
            });
            context.StockBalances.AddRange(
                new StockBalance
                {
                    Id = 7,
                    Product = product,
                    Lot = new Lot { Id = 8, Product = product, LotNo = "LOT-B", Qty = 4m },
                    Location = new Location { Id = 9, Zone = zoneB, Code = "LOC-B-05", Name = "B-05" },
                    QtyAvailable = 4m
                },
                new StockBalance
                {
                    Id = 10,
                    Product = product,
                    Lot = new Lot { Id = 11, Product = product, LotNo = "LOT-A-10", Qty = 4m },
                    Location = new Location { Id = 12, Zone = zoneA, Code = "LOC-A-10", Name = "A-10" },
                    QtyAvailable = 4m
                },
                new StockBalance
                {
                    Id = 13,
                    Product = product,
                    Lot = new Lot { Id = 14, Product = product, LotNo = "LOT-A-01", Qty = 3m },
                    Location = new Location { Id = 15, Zone = zoneA, Code = "LOC-A-01", Name = "A-01" },
                    QtyAvailable = 3m
                });
            await context.SaveChangesAsync();
        }

        await using var assertionContext = new ApplicationDbContext(options);
        var pickList = await new PickListService(assertionContext).CreatePickListForSalesOrderAsync(6);

        Assert.NotNull(pickList);
        var locationsById = await assertionContext.Locations.ToDictionaryAsync(location => location.Id, location => location.Code);
        Assert.Equal(
            ["LOC-A-01", "LOC-A-10", "LOC-B-05"],
            pickList!.Items.OrderBy(item => item.SequenceOrder).Select(item => locationsById[item.LocationId]));
        Assert.Equal([1, 2, 3], pickList.Items.OrderBy(item => item.SequenceOrder).Select(item => item.SequenceOrder));
        Assert.Equal([3m, 4m, 2m], pickList.Items.OrderBy(item => item.SequenceOrder).Select(item => item.QtyToPick));
        Assert.Equal(9m, pickList.Items.Sum(item => item.QtyToPick));
    }

    [Fact]
    public async Task ExportStockValuationExcel_ReturnsValidSpreadsheet()
    {
        await SeedExportStockBalanceAsync();
        using var client = _factory.CreateInventoryClient("Warehouse");
        using var response = await client.GetAsync("/Report/ExportStockValuationExcel");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            response.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(response.Content.Headers.ContentDisposition);
        Assert.EndsWith(
            ".xlsx",
            response.Content.Headers.ContentDisposition!.FileName!.Trim('"'),
            StringComparison.OrdinalIgnoreCase);
        using var workbook = new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
        var worksheet = Assert.Single(workbook.Worksheets);
        Assert.Equal("Báo cáo Tài chính Kho", worksheet.Name);
        Assert.Equal("SKU-EXPORT", worksheet.Cell(4, 1).GetString());
        Assert.Equal("Export product", worksheet.Cell(4, 2).GetString());
        Assert.Equal("Export warehouse", worksheet.Cell(4, 3).GetString());
        Assert.Equal("LOC-EXPORT-01", worksheet.Cell(4, 4).GetString());
        Assert.Equal(12.5m, worksheet.Cell(4, 6).GetValue<decimal>());
        Assert.Equal(1_234.56m, worksheet.Cell(4, 7).GetValue<decimal>());
        Assert.Equal(15_432m, worksheet.Cell(4, 8).GetValue<decimal>());
        Assert.Equal("#,##0.00", worksheet.Cell(4, 6).Style.NumberFormat.Format);
        Assert.Equal("#,##0.00", worksheet.Cell(4, 7).Style.NumberFormat.Format);
        Assert.Equal("#,##0.00", worksheet.Cell(4, 8).Style.NumberFormat.Format);
    }

    [Fact]
    public async Task SendNotificationAsync_IncreasesUnreadCountAndPersistsUnreadNotification()
    {
        await using var context = new ApplicationDbContext(CreateOptions());
        context.AppNotifications.Add(new AppNotification
        {
            Id = 1,
            Title = "Already read",
            Message = "Historical notification",
            IsRead = true
        });
        await context.SaveChangesAsync();
        var service = new NotificationService(context);
        var unreadBefore = await service.GetUnreadCountAsync();

        await service.SendNotificationAsync("Low stock", "SKU-EXPORT is below minimum", "Warning", "/Inventory");

        Assert.Equal(unreadBefore + 1, await service.GetUnreadCountAsync());
        var notification = await context.AppNotifications.SingleAsync(item => item.Title == "Low stock");
        Assert.False(notification.IsRead);
    }

    private static DbContextOptions<ApplicationDbContext> CreateOptions()
    {
        return new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
    }

    private async Task SeedExportStockBalanceAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await using var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var warehouse = new Warehouse { Id = 9_001, Code = "WH-EXPORT", Name = "Export warehouse" };
        var zone = new Zone { Id = 9_002, Warehouse = warehouse, Code = "ZONE-EXPORT", Name = "Export zone" };
        var product = new Product { Id = 9_003, Code = "SKU-EXPORT", Name = "Export product", BaseUomId = 1 };
        context.StockBalances.Add(new StockBalance
        {
            Id = 9_004,
            Product = product,
            Lot = new Lot { Id = 9_005, Product = product, LotNo = "LOT-EXPORT", UnitPrice = 1_234.56m },
            Location = new Location { Id = 9_006, Zone = zone, Code = "LOC-EXPORT-01", Name = "Export location" },
            QtyAvailable = 12.5m
        });
        await context.SaveChangesAsync();
    }
}

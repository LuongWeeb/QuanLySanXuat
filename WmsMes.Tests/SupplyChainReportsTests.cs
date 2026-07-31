using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
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
                Items = [new SalesOrderItem { Product = product, Qty = 11m }]
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
    }

    [Fact]
    public async Task ExportStockValuationExcel_ReturnsValidSpreadsheet()
    {
        using var client = _factory.CreateInventoryClient("Warehouse");

        var response = await client.GetAsync("/Report/ExportStockValuationExcel");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            response.Content.Headers.ContentType?.MediaType);
        using var workbook = new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
        var worksheet = Assert.Single(workbook.Worksheets);
        Assert.Equal("Báo cáo Tài chính Kho", worksheet.Name);
        Assert.Equal("TỔNG CỘNG", worksheet.Cell(4, 7).GetString());
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
}

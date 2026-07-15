using Microsoft.EntityFrameworkCore;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Domain.Enums;
using WmsMes.Web.Services;

namespace WmsMes.Tests;

public class InventoryServiceTests
{
    [Fact]
    public async Task GetSuggestedLotsAsync_UsesFefoAndSplitsRequestedQuantity()
    {
        await using var context = CreateContext();
        await SeedRequiredMasterDataAsync(context);
        context.Products.Add(new Product
        {
            Id = 2,
            Code = "MILK",
            Name = "Milk",
            BaseUomId = 1,
            ShelfLifeDays = 30,
            IsLotTracked = true
        });
        context.Lots.AddRange(
            new Lot { Id = 1, ProductId = 2, LotNo = "LATE", ExpiryDate = DateTime.UtcNow.AddDays(20), Qty = 10 },
            new Lot { Id = 2, ProductId = 2, LotNo = "EARLY", ExpiryDate = DateTime.UtcNow.AddDays(5), Qty = 10 });
        context.StockBalances.AddRange(
            new StockBalance { ProductId = 2, LotId = 1, LocationId = 1, QtyAvailable = 10 },
            new StockBalance { ProductId = 2, LotId = 2, LocationId = 1, QtyAvailable = 10 });
        await context.SaveChangesAsync();

        var service = new InventoryService(context);

        var suggestions = (await service.GetSuggestedLotsAsync(2, 15)).ToList();

        Assert.Equal(2, suggestions.Count);
        Assert.Equal(2, suggestions[0].LotId);
        Assert.Equal(10, suggestions[0].QtyAvailable);
        Assert.Equal(1, suggestions[1].LotId);
        Assert.Equal(5, suggestions[1].QtyAvailable);
    }

    [Fact]
    public async Task CompleteGoodsReceiptAsync_CreatesLotBalanceAndReceiptTransaction()
    {
        await using var context = CreateContext();
        await SeedRequiredMasterDataAsync(context);
        context.GoodsReceipts.Add(new GoodsReceipt
        {
            Id = 1,
            ReceiptNo = "GR-001",
            Status = DocumentStatus.Draft,
            Lines =
            {
                new GoodsReceiptLine
                {
                    ProductId = 1,
                    LocationId = 1,
                    LotNo = "LOT-001",
                    Qty = 12,
                    ExpiryDate = DateTime.UtcNow.AddDays(90)
                }
            }
        });
        await context.SaveChangesAsync();

        var service = new InventoryService(context);

        var completed = await service.CompleteGoodsReceiptAsync(1, "user-1");

        Assert.True(completed);
        var lot = await context.Lots.SingleAsync(l => l.LotNo == "LOT-001");
        Assert.Equal(12, lot.Qty);
        var balance = await context.StockBalances.SingleAsync(sb => sb.LotId == lot.Id);
        Assert.Equal(12, balance.QtyAvailable);
        var transaction = await context.StockTransactions.SingleAsync();
        Assert.Equal(TransactionType.Receipt, transaction.Type);
        Assert.Equal(12, transaction.Qty);
        Assert.Equal("GR-001", transaction.ReferenceNo);
        Assert.Equal(DocumentStatus.Completed, (await context.GoodsReceipts.FindAsync(1))!.Status);
    }

    [Fact]
    public async Task CompleteGoodsIssueAsync_ThrowsInvalidOperationException_WhenStockIsInsufficient()
    {
        await using var context = CreateContext();
        await SeedRequiredMasterDataAsync(context);
        context.Lots.Add(new Lot { Id = 1, ProductId = 1, LotNo = "LOT-001", Qty = 10 });
        context.StockBalances.Add(new StockBalance { ProductId = 1, LotId = 1, LocationId = 1, QtyAvailable = 10 });
        context.GoodsIssues.Add(new GoodsIssue
        {
            Id = 1,
            IssueNo = "GI-001",
            Status = DocumentStatus.Draft,
            Lines =
            {
                new GoodsIssueLine { ProductId = 1, LotId = 1, LocationId = 1, Qty = 15 }
            }
        });
        await context.SaveChangesAsync();

        var service = new InventoryService(context);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CompleteGoodsIssueAsync(1, "user-1"));

        Assert.Equal("Not enough available stock. Negative stock is not allowed.", exception.Message);
        Assert.Equal(10, (await context.StockBalances.SingleAsync()).QtyAvailable);
        Assert.Empty(context.StockTransactions);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static async Task SeedRequiredMasterDataAsync(ApplicationDbContext context)
    {
        context.UnitOfMeasures.Add(new UnitOfMeasure { Id = 1, Code = "PCS", Name = "Pieces" });
        context.Products.Add(new Product { Id = 1, Code = "P01", Name = "Product 01", BaseUomId = 1 });
        context.Warehouses.Add(new Warehouse { Id = 1, Code = "WH01", Name = "Main Warehouse" });
        context.Zones.Add(new Zone { Id = 1, Code = "Z01", Name = "Zone 01", WarehouseId = 1 });
        context.Locations.Add(new Location { Id = 1, Code = "LOC01", Name = "Location 01", ZoneId = 1 });
        await context.SaveChangesAsync();
    }
}

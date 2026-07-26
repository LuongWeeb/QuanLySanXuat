using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Domain.Enums;
using WmsMes.Web.Services;

namespace WmsMes.Tests;

public class MesCoreServiceTests
{
    [Fact]
    public async Task ApproveWorkOrderAsync_ConcurrentRelationalApprovals_ReserveStockOnlyOnce()
    {
        var database = $"file:wo-{Guid.NewGuid():N}?mode=memory&cache=shared";
        await using var keepAlive = new SqliteConnection($"Data Source={database}");
        await keepAlive.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite($"Data Source={database}").Options;
        await using (var seed = new ApplicationDbContext(options))
        {
            await seed.Database.EnsureCreatedAsync();
            await SeedCommonDataAsync(seed);
            await SeedBomRoutingAndWorkOrderAsync(seed);
            seed.WorkOrders.Add(new WorkOrder { Id = 101, Code = "WO-002", ProductId = 1, Qty = 10, DueDate = DateTime.Today, Status = WorkOrderStatus.Draft });
            seed.Lots.Add(new Lot { Id = 11, ProductId = 2, LotNo = "MAT-CONCURRENT", Qty = 13 });
            seed.StockBalances.Add(new StockBalance { ProductId = 2, LotId = 11, LocationId = 77, QtyAvailable = 13 });
            await seed.SaveChangesAsync();
        }

        async Task<bool> Approve(int id)
        {
            await using var context = new ApplicationDbContext(options);
            try { return await new WorkOrderService(context).ApproveWorkOrderAsync(id, "planner"); }
            catch (SqliteException) { return false; }
            catch (InvalidOperationException) { return false; }
        }

        var results = await Task.WhenAll(Approve(100), Approve(101));

        Assert.Single(results.Where(result => result));
        await using var verify = new ApplicationDbContext(options);
        Assert.Equal(13, (await verify.MaterialReservations.Select(x => x.QtyReserved).ToListAsync()).Sum());
        var balance = await verify.StockBalances.SingleAsync(x => x.LotId == 11);
        Assert.Equal(0, balance.QtyAvailable);
        Assert.Equal(13, balance.QtyReserved);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task CalculateRequirementsAsync_AppliesBomScrapAndSubtractsAvailableStock()
    {
        await using var context = CreateContext();
        await SeedCommonDataAsync(context);

        context.BOMs.Add(new BOM
        {
            ProductId = 1,
            Version = "V1.0",
            IsActive = true,
            Items =
            {
                new BOMItem { ComponentProductId = 2, QtyPer = 2.5m, ScrapPercent = 10m }
            }
        });
        context.StockBalances.Add(new StockBalance
        {
            ProductId = 2,
            LotId = 10,
            LocationId = 1,
            QtyAvailable = 20m
        });
        await context.SaveChangesAsync();

        var service = new MrpService(context);

        var result = Assert.Single(await service.CalculateRequirementsAsync(1, 10m));

        Assert.Equal(27.5m, result.GrossDemand);
        Assert.Equal(20m, result.StockAvailable);
        Assert.Equal(7.5m, result.NetDemand);
        Assert.Equal("MAT-01", result.ComponentCode);
    }

    [Fact]
    public async Task CalculateRequirementsAsync_AggregatesLegacyDuplicateComponentsBeforeSubtractingStock()
    {
        await using var context = CreateContext();
        await SeedCommonDataAsync(context);
        context.BOMs.Add(new BOM
        {
            ProductId = 1,
            Version = "LEGACY",
            IsActive = true,
            Items =
            {
                new BOMItem { ComponentProductId = 2, QtyPer = 5m, ScrapPercent = 0m },
                new BOMItem { ComponentProductId = 2, QtyPer = 5m, ScrapPercent = 0m }
            }
        });
        context.StockBalances.Add(new StockBalance
        {
            ProductId = 2,
            LotId = 10,
            LocationId = 77,
            QtyAvailable = 8m
        });
        await context.SaveChangesAsync();

        var result = Assert.Single(
            await new MrpService(context).CalculateRequirementsAsync(1, 1m));

        Assert.Equal(10m, result.GrossDemand);
        Assert.Equal(8m, result.StockAvailable);
        Assert.Equal(2m, result.NetDemand);
    }

    [Fact]
    public async Task ApproveWorkOrderAsync_ReservesMaterialByFefoThenFifoAndCreatesRoutingSteps()
    {
        await using var context = CreateContext();
        await SeedCommonDataAsync(context);
        await SeedBomRoutingAndWorkOrderAsync(context);

        context.Lots.AddRange(
            new Lot { Id = 11, ProductId = 2, LotNo = "LOT-LATE", ExpiryDate = DateTime.Today.AddDays(30), Qty = 8m },
            new Lot { Id = 12, ProductId = 2, LotNo = "LOT-EARLY", ExpiryDate = DateTime.Today.AddDays(5), Qty = 5m });
        context.StockBalances.AddRange(
            new StockBalance { ProductId = 2, LotId = 11, LocationId = 1, QtyAvailable = 8m },
            new StockBalance { ProductId = 2, LotId = 12, LocationId = 1, QtyAvailable = 5m });
        await context.SaveChangesAsync();

        var service = new WorkOrderService(context);

        var approved = await service.ApproveWorkOrderAsync(100, "planner");

        Assert.True(approved);
        var workOrder = await context.WorkOrders.Include(w => w.Steps).SingleAsync(w => w.Id == 100);
        Assert.Equal(WorkOrderStatus.Approved, workOrder.Status);
        Assert.Equal("V1.0", workOrder.BomVersion);
        Assert.Equal("R1", workOrder.RoutingVersion);
        Assert.Equal(new[] { 10, 20 }, workOrder.Steps.OrderBy(s => s.StepNumber).Select(s => s.StepNumber));

        var reservations = await context.MaterialReservations.OrderBy(r => r.LotId).ToListAsync();
        Assert.Equal(2, reservations.Count);
        Assert.Equal(8m, reservations.Single(r => r.LotId == 11).QtyReserved);
        Assert.Equal(5m, reservations.Single(r => r.LotId == 12).QtyReserved);
        Assert.Equal(0m, (await context.StockBalances.SingleAsync(sb => sb.LotId == 11)).QtyAvailable);
        Assert.Equal(0m, (await context.StockBalances.SingleAsync(sb => sb.LotId == 12)).QtyAvailable);
    }

    [Fact]
    public async Task ApproveWorkOrderAsync_ThrowsWhenAvailableMaterialIsInsufficient()
    {
        await using var context = CreateContext();
        await SeedCommonDataAsync(context);
        await SeedBomRoutingAndWorkOrderAsync(context);
        context.StockBalances.Add(new StockBalance { ProductId = 2, LotId = 10, LocationId = 1, QtyAvailable = 3m });
        await context.SaveChangesAsync();

        var service = new WorkOrderService(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApproveWorkOrderAsync(100, "planner"));
    }

    [Fact]
    public async Task StartStepAsync_RequiresPreviousStepsToBeCompleted()
    {
        await using var context = CreateContext();
        await SeedCommonDataAsync(context);
        context.WorkOrders.Add(new WorkOrder
        {
            Id = 100,
            Code = "WO-001",
            ProductId = 1,
            Qty = 1m,
            DueDate = DateTime.Today,
            Status = WorkOrderStatus.Approved
        });
        context.WorkOrderSteps.AddRange(
            new WorkOrderStep { Id = 1, WorkOrderId = 100, StepNumber = 10, StepName = "Mix", WorkCenterId = 1 },
            new WorkOrderStep { Id = 2, WorkOrderId = 100, StepNumber = 20, StepName = "Pack", WorkCenterId = 1 });
        await context.SaveChangesAsync();

        var service = new WorkOrderService(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartStepAsync(2));
    }

    [Fact]
    public async Task CompleteWorkOrderAsync_BackflushesReservationsAndCreatesFinishedLotGenealogy()
    {
        await using var context = CreateContext();
        await SeedCommonDataAsync(context);
        await SeedBomRoutingAndWorkOrderAsync(context);
        context.WorkOrders.Single(w => w.Id == 100).Status = WorkOrderStatus.InProgress;
        context.Lots.Single(lot => lot.Id == 10).UnitPrice = 6.5m;
        context.WorkOrderSteps.AddRange(
            new WorkOrderStep
            {
                WorkOrderId = 100,
                StepNumber = 10,
                StepName = "Mix",
                WorkCenterId = 1,
                Status = WorkOrderStepStatus.Completed,
                QtyOK = 9m
            },
            new WorkOrderStep
            {
                WorkOrderId = 100,
                StepNumber = 20,
                StepName = "Pack",
                WorkCenterId = 1,
                Status = WorkOrderStepStatus.Completed,
                QtyOK = 8m
            });
        context.MaterialReservations.Add(new MaterialReservation
        {
            WorkOrderId = 100,
            ProductId = 2,
            LotId = 10,
            LocationId = 1,
            QtyReserved = 12m
        });
        context.StockBalances.Add(new StockBalance
        {
            ProductId = 2,
            LotId = 10,
            LocationId = 1,
            QtyAvailable = 5m,
            QtyReserved = 15m,
            QtyOnHold = 4m
        });
        await context.SaveChangesAsync();

        var service = new WorkOrderService(context);

        var completed = await service.CompleteWorkOrderAsync(100, "worker");

        Assert.True(completed);
        var workOrder = await context.WorkOrders.SingleAsync(w => w.Id == 100);
        Assert.Equal(WorkOrderStatus.Completed, workOrder.Status);

        var outputLot = await context.Lots.SingleAsync(l => l.WorkOrderId == 100);
        Assert.StartsWith("FG-01-" + DateTime.Today.ToString("yyyyMMdd"), outputLot.LotNo);
        Assert.Equal(8m, outputLot.Qty);
        var manufacturedBalance = await context.StockBalances
            .SingleAsync(sb => sb.LotId == outputLot.Id);
        Assert.Equal(77, manufacturedBalance.LocationId);
        var manufacturedReceipt = await context.StockTransactions
            .SingleAsync(tx => tx.LotId == outputLot.Id);
        Assert.Equal(77, manufacturedReceipt.LocationId);
        Assert.Equal(manufacturedBalance.QtyAvailable, manufacturedReceipt.QtyAfter);
        Assert.Equal(outputLot.UnitPrice, manufacturedReceipt.ValuationRate);

        var materialBalance = await context.StockBalances
            .SingleAsync(sb => sb.LotId == 10);
        Assert.Equal(5m, materialBalance.QtyAvailable);
        Assert.Equal(3m, materialBalance.QtyReserved);
        Assert.Equal(4m, materialBalance.QtyOnHold);
        var backflush = await context.StockTransactions
            .SingleAsync(tx => tx.Type == TransactionType.Backflush);
        Assert.Equal(-12m, backflush.Qty);
        Assert.Equal(materialBalance.QtyAvailable, backflush.QtyAfter);
        Assert.Equal(6.5m, backflush.ValuationRate);
        Assert.Contains(await context.LotGenealogies.ToListAsync(), g => g.OutputLotId == outputLot.Id && g.InputLotId == 10 && g.QtyConsumed == 12m);
    }

    private static async Task SeedCommonDataAsync(ApplicationDbContext context)
    {
        context.UnitOfMeasures.Add(new UnitOfMeasure { Id = 1, Code = "PCS", Name = "Pieces" });
        context.Products.AddRange(
            new Product { Id = 1, Code = "FG-01", Name = "Finished Good", BaseUomId = 1, IsManufactured = true },
            new Product { Id = 2, Code = "MAT-01", Name = "Material", BaseUomId = 1 });
        context.Warehouses.Add(new Warehouse { Id = 1, Code = "WH", Name = "Main Warehouse" });
        context.Zones.Add(new Zone { Id = 1, WarehouseId = 1, Code = "FG", Name = "Finished Goods" });
        context.Locations.Add(new Location { Id = 77, ZoneId = 1, Code = "LOC-FG-01", Name = "Default Finished Goods" });
        context.Lots.Add(new Lot { Id = 10, ProductId = 2, LotNo = "MAT-BASE", Qty = 100m });
        context.WorkCenters.Add(new WorkCenter { Id = 1, Code = "WC-01", Name = "Line 1" });
        await context.SaveChangesAsync();
    }

    private static async Task SeedBomRoutingAndWorkOrderAsync(ApplicationDbContext context)
    {
        context.BOMs.Add(new BOM
        {
            ProductId = 1,
            Version = "V1.0",
            IsActive = true,
            Items =
            {
                new BOMItem { ComponentProductId = 2, QtyPer = 1.3m, ScrapPercent = 0m }
            }
        });
        context.Routings.Add(new Routing
        {
            ProductId = 1,
            Name = "Default Routing",
            Version = "R1",
            IsActive = true,
            Steps =
            {
                new RoutingStep { StepNumber = 10, StepName = "Mix", WorkCenterId = 1 },
                new RoutingStep { StepNumber = 20, StepName = "Pack", WorkCenterId = 1 }
            }
        });
        context.WorkOrders.Add(new WorkOrder
        {
            Id = 100,
            Code = "WO-001",
            ProductId = 1,
            Qty = 10m,
            DueDate = DateTime.Today,
            Status = WorkOrderStatus.Draft
        });
        await context.SaveChangesAsync();
    }
}

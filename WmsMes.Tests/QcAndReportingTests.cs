using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Domain.Enums;
using WmsMes.Web.Services;
using WmsMes.Web.Hubs;

namespace WmsMes.Tests;

public class QcAndReportingTests
{
    [Theory]
    [InlineData(QCResult.PASS)]
    [InlineData(QCResult.REJECT)]
    public async Task SubmitQCInspectionAsync_WhenPostCommitNotificationFails_ReturnsSuccessAndLogs(QCResult result)
    {
        await using var context = CreateContext();
        await SeedQcDataAsync(context);
        var client = new Mock<IClientProxy>();
        client.Setup(x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("hub unavailable"));
        var clients = new Mock<IHubClients>();
        clients.SetupGet(x => x.All).Returns(client.Object);
        var inventoryHub = new Mock<IHubContext<InventoryHub>>();
        inventoryHub.SetupGet(x => x.Clients).Returns(clients.Object);
        var logger = new Mock<ILogger<QcService>>();
        var service = new QcService(context, new CostingService(context), null, inventoryHub.Object, logger.Object);
        var inspection = new QCInspection
        {
            WorkOrderId = 100, LotId = 20, Result = result,
            Lines = { new QCInspectionLine { ParameterName = "Do am", ValueInspected = result == QCResult.PASS ? "12" : "20" } }
        };

        Assert.True(await service.SubmitQCInspectionAsync(inspection, "qc-user"));
        context.ChangeTracker.Clear();
        Assert.Equal(result, (await context.QCInspections.SingleAsync()).Result);
        logger.Verify(x => x.Log(LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    [Fact]
    public async Task SubmitQCInspectionAsync_NotifiesInventoryDashboard_WhenInspectionPasses()
    {
        await using var context = CreateContext();
        await SeedQcDataAsync(context);
        var client = new Mock<IClientProxy>();
        var clients = new Mock<IHubClients>();
        clients.SetupGet(x => x.All).Returns(client.Object);
        var inventoryHub = new Mock<IHubContext<InventoryHub>>();
        inventoryHub.SetupGet(x => x.Clients).Returns(clients.Object);
        var service = new QcService(context, new CostingService(context), null, inventoryHub.Object);
        var inspection = new QCInspection
        {
            WorkOrderId = 100, LotId = 20, Result = QCResult.PASS,
            Lines = { new QCInspectionLine { ParameterName = "Do am", ValueInspected = "12" } }
        };

        Assert.True(await service.SubmitQCInspectionAsync(inspection, "qc-user"));

        client.Verify(x => x.SendCoreAsync("ReceiveStockUpdate", It.Is<object?[]>(a => a.Length == 0), default), Times.Once);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task CompleteWorkOrderAsync_PutsFinishedLotOnHoldForQc()
    {
        await using var context = CreateContext();
        await SeedManufacturingDataAsync(context);

        var service = new WorkOrderService(context);

        var completed = await service.CompleteWorkOrderAsync(100, "worker");

        Assert.True(completed);
        var outputLot = await context.Lots.SingleAsync(l => l.WorkOrderId == 100);
        var outputBalance = await context.StockBalances.SingleAsync(sb => sb.LotId == outputLot.Id);
        Assert.Equal(0m, outputBalance.QtyAvailable);
        Assert.Equal(8m, outputBalance.QtyOnHold);
    }

    [Fact]
    public async Task SubmitQCInspectionAsync_ReleasesHoldAndSetsUnitCost_WhenInspectionPasses()
    {
        await using var context = CreateContext();
        await SeedQcDataAsync(context);

        var service = new QcService(context, new CostingService(context));
        var inspection = new QCInspection
        {
            WorkOrderId = 100,
            LotId = 20,
            Result = QCResult.PASS,
            Lines =
            {
                new QCInspectionLine { ParameterName = "Do am", ValueInspected = "12" }
            }
        };

        var submitted = await service.SubmitQCInspectionAsync(inspection, "qc-user");

        Assert.True(submitted);
        var balance = await context.StockBalances.SingleAsync(sb => sb.LotId == 20);
        Assert.Equal(8m, balance.QtyAvailable);
        Assert.Equal(0m, balance.QtyOnHold);
        Assert.Equal(150m, (await context.Lots.SingleAsync(l => l.Id == 20)).UnitPrice);
        Assert.True((await context.QCInspections.Include(i => i.Lines).SingleAsync()).Lines.Single().IsOK);
    }

    [Fact]
    public async Task SubmitQCInspectionAsync_MovesRejectedHoldToQuarantine()
    {
        await using var context = CreateContext();
        await SeedQcDataAsync(context);

        var service = new QcService(context, new CostingService(context));
        var inspection = new QCInspection
        {
            WorkOrderId = 100,
            LotId = 20,
            Result = QCResult.PASS,
            Note = "Ngoai nguong",
            Lines =
            {
                new QCInspectionLine { ParameterName = "Do am", ValueInspected = "20" }
            }
        };

        var submitted = await service.SubmitQCInspectionAsync(inspection, "qc-user");

        Assert.True(submitted);
        var balances = await context.StockBalances.Where(sb => sb.LotId == 20).ToListAsync();
        Assert.Equal(0m, balances.Sum(x => x.QtyAvailable));
        Assert.Equal(8m, balances.Single(x => x.LocationId == 2).QtyOnHold);
        Assert.Equal(0m, balances.Single(x => x.LocationId == 1).QtyOnHold);
        Assert.Equal(QCResult.REJECT, (await context.QCInspections.SingleAsync()).Result);
        Assert.Contains(await context.StockTransfers.Include(t => t.Lines).ToListAsync(),
            transfer => transfer.Lines.Any(line => line.LotId == 20 && line.ToLocationId == 2 && line.Qty == 8m));
    }

    [Fact]
    public async Task SubmitQCInspectionAsync_WritesQuarantineBalanceAndLotValuationToTransferLedger()
    {
        await using var context = CreateContext();
        await SeedQcDataAsync(context);
        (await context.Lots.SingleAsync(lot => lot.Id == 20)).UnitPrice = 42.5m;
        await context.SaveChangesAsync();
        var inspection = new QCInspection
        {
            WorkOrderId = 100,
            LotId = 20,
            Result = QCResult.REJECT,
            Lines =
            {
                new QCInspectionLine
                {
                    ParameterName = "Do am",
                    ValueInspected = "20"
                }
            }
        };

        Assert.True(await new QcService(context, new CostingService(context))
            .SubmitQCInspectionAsync(inspection, "qc-user"));

        var transaction = await context.StockTransactions.SingleAsync();
        Assert.Equal(TransactionType.Transfer, transaction.Type);
        Assert.Equal(8m, transaction.QtyAfter);
        Assert.Equal(42.5m, transaction.ValuationRate);
    }

    [Fact]
    public async Task SubmitQCInspectionAsync_AllowsHistoricalInspectionAfterLotIsPlacedOnHoldAgain()
    {
        await using var context = CreateContext(); await SeedQcDataAsync(context); var service = new QcService(context, new CostingService(context));
        var first = new QCInspection { WorkOrderId=100,LotId=20,Result=QCResult.PASS,Lines={new QCInspectionLine{ParameterName="Do am",ValueInspected="12"}}};
        var second = new QCInspection { WorkOrderId=100,LotId=20,Result=QCResult.REJECT,Lines={new QCInspectionLine{ParameterName="Do am",ValueInspected="20"}}};
        Assert.True(await service.SubmitQCInspectionAsync(first,"qc-1"));
        var balance=await context.StockBalances.SingleAsync(x=>x.LotId==20); balance.QtyOnHold=3; await context.SaveChangesAsync();
        Assert.True(await service.SubmitQCInspectionAsync(second,"qc-2"));
        Assert.Equal(2,await context.QCInspections.CountAsync());
    }

    [Fact]
    public void QcInspectionModel_DoesNotForbidHistoricalInspectionsForLot()
    {
        using var context=CreateContext(); var entity=context.Model.FindEntityType(typeof(QCInspection))!;
        Assert.DoesNotContain(entity.GetIndexes(),x=>x.IsUnique && x.Properties.Select(p=>p.Name).SequenceEqual(new[]{nameof(QCInspection.LotId)}));
    }

    [Fact]
    public async Task SubmitQCInspectionAsync_ReleasesEveryOnHoldBalanceForLot()
    {
        await using var context=CreateContext(); await SeedQcDataAsync(context); context.Locations.Add(new Location{Id=3,ZoneId=1,Code="FG-02",Name="Second"}); context.StockBalances.Add(new StockBalance{ProductId=1,LotId=20,LocationId=3,QtyOnHold=2}); await context.SaveChangesAsync();
        var service=new QcService(context,new CostingService(context)); var inspection=new QCInspection{WorkOrderId=100,LotId=20,Result=QCResult.PASS,Lines={new QCInspectionLine{ParameterName="Do am",ValueInspected="12"}}};
        Assert.True(await service.SubmitQCInspectionAsync(inspection,"qc"));
        var balances=await context.StockBalances.Where(x=>x.LotId==20).ToListAsync(); Assert.All(balances,x=>Assert.Equal(0,x.QtyOnHold)); Assert.Equal(10,balances.Sum(x=>x.QtyAvailable));
    }

    [Fact]
    public async Task SubmitQCInspectionAsync_RejectConsolidatesMultipleSourcesIntoExistingQuarantineBalance()
    {
        await using var context=CreateContext(); await SeedQcDataAsync(context);
        context.Locations.Add(new Location{Id=3,ZoneId=1,Code="FG-02",Name="Second"});
        context.StockBalances.AddRange(new StockBalance{ProductId=1,LotId=20,LocationId=3,QtyOnHold=2},new StockBalance{ProductId=1,LotId=20,LocationId=2,QtyOnHold=1,QtyAvailable=4}); await context.SaveChangesAsync();
        var service=new QcService(context,new CostingService(context)); var inspection=new QCInspection{WorkOrderId=100,LotId=20,Result=QCResult.REJECT,Lines={new QCInspectionLine{ParameterName="Do am",ValueInspected="20"}}};
        Assert.True(await service.SubmitQCInspectionAsync(inspection,"qc"));
        var balances=await context.StockBalances.Where(x=>x.LotId==20).OrderBy(x=>x.LocationId).ToListAsync();
        Assert.Equal(3,balances.Count); Assert.Equal(11,balances.Single(x=>x.LocationId==2).QtyOnHold); Assert.Equal(4,balances.Single(x=>x.LocationId==2).QtyAvailable); Assert.All(balances.Where(x=>x.LocationId!=2),x=>Assert.Equal(0,x.QtyOnHold));
    }

    [Fact]
    public async Task SubmitQCInspectionAsync_ConcurrentRelationalClaimsCreateOnlyOneInspection()
    {
        var file=Path.Combine(Path.GetTempPath(), $"qc-{Guid.NewGuid()}.db");
        try
        {
            var options=new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite($"Data Source={file};Default Timeout=10;Pooling=False").Options;
            await using(var seed=new ApplicationDbContext(options)){await seed.Database.EnsureCreatedAsync();await SeedQcDataAsync(seed);}
            async Task<bool> Submit(string user){await using var db=new ApplicationDbContext(options);return await new QcService(db,new CostingService(db)).SubmitQCInspectionAsync(new QCInspection{WorkOrderId=100,LotId=20,Result=QCResult.REJECT,Lines={new QCInspectionLine{ParameterName="Do am",ValueInspected="20"}}},user);}
            var results=await Task.WhenAll(Submit("qc-1"),Submit("qc-2"));
            Assert.Single(results.Where(x=>x)); await using var verify=new ApplicationDbContext(options); Assert.Single(await verify.QCInspections.ToListAsync()); Assert.All(await verify.StockBalances.Where(x=>x.LotId==20 && x.Location!.Code!=QcService.QuarantineLocationCode).ToListAsync(),x=>Assert.Equal(0,x.QtyOnHold));
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if(File.Exists(file)) File.Delete(file); }
    }

    [Fact]
    public async Task CalculateProductionCostAsync_UsesInputLotGenealogyAndActualLotPrices()
    {
        await using var context = CreateContext();
        await SeedQcDataAsync(context);

        var service = new CostingService(context);

        var cost = await service.CalculateProductionCostAsync(100);

        Assert.Equal(150m, cost);
    }

    [Fact]
    public async Task GetBackwardTraceAsync_ReturnsRecursiveInputLotTree()
    {
        await using var context = CreateContext();
        await SeedQcDataAsync(context);
        var service = new TraceabilityService(context);

        var root = await service.GetBackwardTraceAsync("FG-LOT");

        Assert.NotNull(root);
        Assert.Equal("FG-LOT", root.LotNo);
        var material = Assert.Single(root.Children);
        Assert.Equal("MAT-LOT", material.LotNo);
        Assert.Equal(12m, material.Qty);
    }

    private static async Task SeedManufacturingDataAsync(ApplicationDbContext context)
    {
        await SeedCommonMasterDataAsync(context);
        context.WorkOrders.Add(new WorkOrder
        {
            Id = 100,
            Code = "WO-001",
            ProductId = 1,
            Qty = 8m,
            DueDate = DateTime.Today,
            Status = WorkOrderStatus.InProgress
        });
        context.WorkOrderSteps.Add(new WorkOrderStep
        {
            Id = 1000,
            WorkOrderId = 100,
            StepNumber = 10,
            StepName = "Dong goi",
            WorkCenterId = 1,
            QtyOK = 8m,
            Status = WorkOrderStepStatus.Completed
        });
        context.Lots.Add(new Lot { Id = 10, ProductId = 2, LotNo = "MAT-LOT", Qty = 100m, UnitPrice = 100m });
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
            QtyReserved = 12m
        });
        await context.SaveChangesAsync();
    }

    private static async Task SeedQcDataAsync(ApplicationDbContext context)
    {
        await SeedCommonMasterDataAsync(context);
        context.WorkOrders.Add(new WorkOrder
        {
            Id = 100,
            Code = "WO-001",
            ProductId = 1,
            Qty = 8m,
            DueDate = DateTime.Today,
            Status = WorkOrderStatus.Completed
        });
        context.Lots.AddRange(
            new Lot { Id = 10, ProductId = 2, LotNo = "MAT-LOT", Qty = 100m, UnitPrice = 100m },
            new Lot { Id = 20, ProductId = 1, LotNo = "FG-LOT", Qty = 8m, WorkOrderId = 100 });
        context.LotGenealogies.Add(new LotGenealogy { OutputLotId = 20, InputLotId = 10, QtyConsumed = 12m });
        context.StockBalances.Add(new StockBalance
        {
            ProductId = 1,
            LotId = 20,
            LocationId = 1,
            QtyAvailable = 0m,
            QtyOnHold = 8m
        });
        context.QCChecklists.Add(new QCChecklist
        {
            Id = 1,
            ProductId = 1,
            Name = "QC thanh pham",
            IsActive = true,
            Items =
            {
                new QCChecklistItem
                {
                    ParameterName = "Do am",
                    MinVal = 10m,
                    MaxVal = 15m,
                    Unit = "%",
                    IsRequired = true
                }
            }
        });
        await context.SaveChangesAsync();
    }

    private static async Task SeedCommonMasterDataAsync(ApplicationDbContext context)
    {
        context.UnitOfMeasures.Add(new UnitOfMeasure { Id = 1, Code = "PCS", Name = "Pieces" });
        context.Products.AddRange(
            new Product { Id = 1, Code = "FG-01", Name = "Finished Good", BaseUomId = 1, IsManufactured = true },
            new Product { Id = 2, Code = "MAT-01", Name = "Material", BaseUomId = 1 });
        context.Warehouses.Add(new Warehouse { Id = 1, Code = "WH", Name = "Main Warehouse" });
        context.Zones.AddRange(
            new Zone { Id = 1, WarehouseId = 1, Code = "FG", Name = "Finished Goods" },
            new Zone { Id = 2, WarehouseId = 1, Code = "QUAR", Name = "Quarantine" });
        context.Locations.AddRange(
            new Location { Id = 1, ZoneId = 1, Code = WorkOrderService.FinishedGoodsQcLocationCode, Name = "Default Finished Goods" },
            new Location { Id = 2, ZoneId = 2, Code = QcService.QuarantineLocationCode, Name = "QC Quarantine" });
        context.WorkCenters.Add(new WorkCenter { Id = 1, Code = "WC-01", Name = "Line 1" });
        await context.SaveChangesAsync();
    }
}

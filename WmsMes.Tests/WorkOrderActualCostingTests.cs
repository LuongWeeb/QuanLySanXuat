using Microsoft.EntityFrameworkCore;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Domain.Enums;
using WmsMes.Web.Services;

namespace WmsMes.Tests;

public class WorkOrderActualCostingTests
{
    [Fact]
    public async Task CompleteWorkOrderAsync_CombinesMaterialAndActualOperationCost_RoundsAwayFromZeroAndValuesReceipt()
    {
        await using var context = CreateContext();
        var start = new DateTime(2026, 7, 27, 1, 0, 0, DateTimeKind.Utc);
        await SeedCompletionAsync(
            context,
            finalQty: 2m,
            laborRate: 1m,
            machineRate: 0m,
            startTime: start,
            endTime: start.AddMinutes(60),
            materialQty: 1m,
            materialUnitPrice: 4.35m);
        AddRouting(context, standardTimeMinutes: 180m);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var completed = await new WorkOrderService(context)
            .CompleteWorkOrderAsync(WorkOrderId, "worker");

        Assert.True(completed);
        var finishedLot = await context.Lots.SingleAsync(lot => lot.WorkOrderId == WorkOrderId);
        var receipt = await context.StockTransactions.SingleAsync(transaction =>
            transaction.Type == TransactionType.Receipt &&
            transaction.LotId == finishedLot.Id);
        Assert.Equal(2.68m, finishedLot.UnitPrice);
        Assert.Equal(finishedLot.UnitPrice, receipt.ValuationRate);
    }

    [Fact]
    public async Task CompleteThenPassQc_PreservesCompletionActualCostOnLotAndReceipt()
    {
        await using var context = CreateContext();
        var start = new DateTime(2026, 7, 27, 1, 0, 0, DateTimeKind.Utc);
        await SeedCompletionAsync(
            context,
            finalQty: 2m,
            laborRate: 1m,
            machineRate: 0m,
            startTime: start,
            endTime: start.AddMinutes(60),
            materialQty: 1m,
            materialUnitPrice: 4.35m);
        context.ChangeTracker.Clear();

        Assert.True(await new WorkOrderService(context)
            .CompleteWorkOrderAsync(WorkOrderId, "worker"));
        var finishedLot = await context.Lots.SingleAsync(lot => lot.WorkOrderId == WorkOrderId);
        Assert.True(await new QcService(context)
            .SubmitQCInspectionAsync(
                new QCInspection
                {
                    WorkOrderId = WorkOrderId,
                    LotId = finishedLot.Id,
                    Result = QCResult.PASS,
                    Lines =
                    {
                        new QCInspectionLine
                        {
                            ParameterName = "Result",
                            ValueInspected = "PASS"
                        }
                    }
                },
                "qc-user"));
        context.ChangeTracker.Clear();

        finishedLot = await context.Lots.SingleAsync(lot => lot.WorkOrderId == WorkOrderId);
        var originalReceipt = await context.StockTransactions.SingleAsync(transaction =>
            transaction.Type == TransactionType.Receipt &&
            transaction.LotId == finishedLot.Id);
        Assert.Equal(2.68m, finishedLot.UnitPrice);
        Assert.Equal(finishedLot.UnitPrice, originalReceipt.ValuationRate);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("zero")]
    [InlineData("negative")]
    public async Task CompleteWorkOrderAsync_WhenActualDurationIsNotPositive_UsesNewestActiveRoutingStandard(
        string actualDuration)
    {
        await using var context = CreateContext();
        var start = new DateTime(2026, 7, 27, 1, 0, 0, DateTimeKind.Utc);
        var (startTime, endTime) = actualDuration switch
        {
            "missing" => ((DateTime?)null, (DateTime?)null),
            "zero" => (start, start),
            "negative" => (start, start.AddMinutes(-10)),
            _ => throw new ArgumentOutOfRangeException(nameof(actualDuration))
        };
        await SeedCompletionAsync(
            context,
            finalQty: 2m,
            laborRate: 12m,
            machineRate: 0m,
            startTime: startTime,
            endTime: endTime);
        AddRouting(context, standardTimeMinutes: 60m);
        await context.SaveChangesAsync();
        AddRouting(context, standardTimeMinutes: 30m);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        await new WorkOrderService(context).CompleteWorkOrderAsync(WorkOrderId, "worker");

        var finishedLot = await context.Lots.SingleAsync(lot => lot.WorkOrderId == WorkOrderId);
        Assert.Equal(3m, finishedLot.UnitPrice);
    }

    [Fact]
    public async Task CompleteWorkOrderAsync_WhenActualAndStandardDurationAreUnavailable_UsesZeroOperationCost()
    {
        await using var context = CreateContext();
        await SeedCompletionAsync(
            context,
            finalQty: 2m,
            laborRate: 12m,
            machineRate: 0m,
            startTime: null,
            endTime: null,
            materialQty: 1m,
            materialUnitPrice: 4m);
        context.ChangeTracker.Clear();

        await new WorkOrderService(context).CompleteWorkOrderAsync(WorkOrderId, "worker");

        var finishedLot = await context.Lots.SingleAsync(lot => lot.WorkOrderId == WorkOrderId);
        Assert.Equal(2m, finishedLot.UnitPrice);
    }

    [Fact]
    public async Task CompleteWorkOrderAsync_WhenRoutingHasDuplicateStepNumbers_UsesHighestStepId()
    {
        await using var context = CreateContext();
        await SeedCompletionAsync(
            context,
            finalQty: 2m,
            laborRate: 12m,
            machineRate: 0m,
            startTime: null,
            endTime: null);
        AddRouting(context, standardTimeMinutes: 60m);
        await context.SaveChangesAsync();
        var routing = await context.Routings.Include(candidate => candidate.Steps).SingleAsync();
        var olderStepId = Assert.Single(routing.Steps).Id;
        var newerStep = new RoutingStep
        {
            RoutingId = routing.Id,
            StepNumber = 10,
            StepName = "Corrected standard",
            WorkCenterId = 1,
            StandardTimeMinutes = 30m
        };
        context.RoutingSteps.Add(newerStep);
        await context.SaveChangesAsync();
        Assert.True(newerStep.Id > olderStepId);
        context.ChangeTracker.Clear();

        Assert.True(await new WorkOrderService(context)
            .CompleteWorkOrderAsync(WorkOrderId, "worker"));

        var finishedLot = await context.Lots.SingleAsync(lot => lot.WorkOrderId == WorkOrderId);
        Assert.Equal(3m, finishedLot.UnitPrice);
    }

    [Fact]
    public async Task CompleteWorkOrderAsync_WhenFinalQuantityIsZero_StoresZeroUnitCost()
    {
        await using var context = CreateContext();
        var start = new DateTime(2026, 7, 27, 1, 0, 0, DateTimeKind.Utc);
        await SeedCompletionAsync(
            context,
            finalQty: 0m,
            laborRate: 12m,
            machineRate: 0m,
            startTime: start,
            endTime: start.AddMinutes(30),
            materialQty: 1m,
            materialUnitPrice: 4m);
        context.ChangeTracker.Clear();

        var completed = await new WorkOrderService(context)
            .CompleteWorkOrderAsync(WorkOrderId, "worker");

        Assert.True(completed);
        var finishedLot = await context.Lots.SingleAsync(lot => lot.WorkOrderId == WorkOrderId);
        var receipt = await context.StockTransactions.SingleAsync(transaction =>
            transaction.Type == TransactionType.Receipt &&
            transaction.LotId == finishedLot.Id);
        Assert.Equal(0m, finishedLot.UnitPrice);
        Assert.Equal(0m, receipt.ValuationRate);
    }

    [Fact]
    public async Task CompleteWorkOrderAsync_WhenFinalStepsShareStepNumber_UsesHighestStepId()
    {
        await using var context = CreateContext();
        await SeedCompletionAsync(
            context,
            finalQty: 2m,
            laborRate: 0m,
            machineRate: 0m,
            startTime: null,
            endTime: null,
            materialQty: 1m,
            materialUnitPrice: 8m);
        context.WorkOrderSteps.Add(new WorkOrderStep
        {
            Id = 1001,
            WorkOrderId = WorkOrderId,
            StepNumber = 10,
            StepName = "Corrected operation result",
            WorkCenterId = 1,
            QtyOK = 4m,
            Status = WorkOrderStepStatus.Completed
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        Assert.True(await new WorkOrderService(context)
            .CompleteWorkOrderAsync(WorkOrderId, "worker"));

        var finishedLot = await context.Lots.SingleAsync(lot => lot.WorkOrderId == WorkOrderId);
        var receipt = await context.StockTransactions.SingleAsync(transaction =>
            transaction.Type == TransactionType.Receipt &&
            transaction.LotId == finishedLot.Id);
        Assert.Equal(4m, finishedLot.Qty);
        Assert.Equal(2m, finishedLot.UnitPrice);
        Assert.Equal(4m, receipt.Qty);
    }

    private const int WorkOrderId = 100;

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static async Task SeedCompletionAsync(
        ApplicationDbContext context,
        decimal finalQty,
        decimal laborRate,
        decimal machineRate,
        DateTime? startTime,
        DateTime? endTime,
        decimal materialQty = 0m,
        decimal materialUnitPrice = 0m)
    {
        context.UnitOfMeasures.Add(new UnitOfMeasure
        {
            Id = 1,
            Code = "PCS",
            Name = "Pieces"
        });
        context.Products.AddRange(
            new Product
            {
                Id = 1,
                Code = "FG-01",
                Name = "Finished Good",
                BaseUomId = 1,
                IsManufactured = true
            },
            new Product
            {
                Id = 2,
                Code = "MAT-01",
                Name = "Material",
                BaseUomId = 1
            });
        context.Warehouses.Add(new Warehouse
        {
            Id = 1,
            Code = "WH",
            Name = "Main Warehouse"
        });
        context.Zones.Add(new Zone
        {
            Id = 1,
            WarehouseId = 1,
            Code = "FG",
            Name = "Finished Goods"
        });
        context.Locations.Add(new Location
        {
            Id = 1,
            ZoneId = 1,
            Code = WorkOrderService.FinishedGoodsQcLocationCode,
            Name = "Finished Goods QC"
        });
        context.WorkCenters.Add(new WorkCenter
        {
            Id = 1,
            Code = "WC-01",
            Name = "Line 1",
            HourlyLaborRate = laborRate,
            HourlyMachineRate = machineRate
        });
        context.WorkOrders.Add(new WorkOrder
        {
            Id = WorkOrderId,
            Code = "WO-001",
            ProductId = 1,
            Qty = finalQty,
            DueDate = DateTime.Today,
            Status = WorkOrderStatus.InProgress
        });
        context.WorkOrderSteps.Add(new WorkOrderStep
        {
            Id = 1000,
            WorkOrderId = WorkOrderId,
            StepNumber = 10,
            StepName = "Operation",
            WorkCenterId = 1,
            StartTime = startTime,
            EndTime = endTime,
            QtyOK = finalQty,
            Status = WorkOrderStepStatus.Completed
        });

        if (materialQty > 0m)
        {
            context.Lots.Add(new Lot
            {
                Id = 10,
                ProductId = 2,
                LotNo = "MAT-LOT",
                Qty = materialQty,
                UnitPrice = materialUnitPrice
            });
            context.MaterialReservations.Add(new MaterialReservation
            {
                WorkOrderId = WorkOrderId,
                ProductId = 2,
                LotId = 10,
                LocationId = 1,
                QtyReserved = materialQty
            });
            context.StockBalances.Add(new StockBalance
            {
                ProductId = 2,
                LotId = 10,
                LocationId = 1,
                QtyReserved = materialQty
            });
        }

        await context.SaveChangesAsync();
    }

    private static void AddRouting(ApplicationDbContext context, decimal standardTimeMinutes)
    {
        context.Routings.Add(new Routing
        {
            ProductId = 1,
            Name = $"Routing {standardTimeMinutes}",
            Version = $"R-{standardTimeMinutes}",
            IsActive = true,
            Steps =
            {
                new RoutingStep
                {
                    StepNumber = 10,
                    StepName = "Operation",
                    WorkCenterId = 1,
                    StandardTimeMinutes = standardTimeMinutes
                }
            }
        });
    }
}

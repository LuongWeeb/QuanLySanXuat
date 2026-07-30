using Microsoft.EntityFrameworkCore;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Domain.Enums;
using WmsMes.Web.Services;

namespace WmsMes.Tests;

public class OeeServiceTests
{
    [Fact]
    public async Task CalculateOee_ReturnsCorrectPercentages()
    {
        await using var context = CreateContext();
        var workCenter = new WorkCenter { Id = 1, Code = "WC-01", Name = "Assembly" };
        context.WorkCenters.Add(workCenter);
        context.WorkOrders.Add(new WorkOrder
        {
            Id = 1,
            Code = "WO-01",
            ProductId = 1,
            DueDate = new DateTime(2026, 7, 31),
            BomVersion = "V1",
            RoutingVersion = "V1"
        });
        context.Routings.Add(CreateRouting(1, 1, workCenter.Id, 2.4m));
        context.WorkOrderSteps.Add(CreateCompletedStep(1, workCenter.Id, 1, 240, 90, 10));
        await context.SaveChangesAsync();

        var metrics = await new OeeService(context).GetWorkCenterOeeAsync(
            workCenter.Id,
            new DateTime(2026, 7, 1),
            new DateTime(2026, 7, 1, 23, 59, 59));

        Assert.Equal(50m, metrics.Availability);
        Assert.Equal(100m, metrics.Performance);
        Assert.Equal(90m, metrics.Quality);
        Assert.Equal(45m, metrics.Oee);
    }

    [Fact]
    public async Task GetWorkCenterOeeAsync_CalculatesRoundedMetricsFromCompletedStepsInPeriod()
    {
        await using var context = CreateContext();
        var workCenter = new WorkCenter { Id = 1, Code = "WC-01", Name = "Assembly" };
        context.WorkCenters.Add(workCenter);
        context.WorkOrders.Add(new WorkOrder
        {
            Id = 1,
            Code = "WO-01",
            ProductId = 1,
            DueDate = new DateTime(2026, 7, 31),
            BomVersion = "V1",
            RoutingVersion = "V1"
        });
        context.Routings.Add(new Routing
        {
            Id = 1,
            ProductId = 1,
            Name = "Assembly routing",
            Version = "V1",
            Steps =
            {
                new RoutingStep
                {
                    StepNumber = 10,
                    StepName = "Assembly",
                    WorkCenterId = workCenter.Id,
                    StandardTimeMinutes = 1m
                }
            }
        });
        context.WorkOrderSteps.AddRange(
            new WorkOrderStep
            {
                WorkOrderId = 1,
                StepNumber = 10,
                StepName = "Assembly",
                WorkCenterId = workCenter.Id,
                StartTime = new DateTime(2026, 7, 1, 8, 0, 0),
                EndTime = new DateTime(2026, 7, 1, 10, 0, 0),
                QtyOK = 90m,
                QtyReject = 10m,
                Status = WorkOrderStepStatus.Completed
            },
            new WorkOrderStep
            {
                WorkOrderId = 1,
                StepNumber = 10,
                StepName = "Outside period",
                WorkCenterId = workCenter.Id,
                StartTime = new DateTime(2026, 6, 30, 8, 0, 0),
                EndTime = new DateTime(2026, 6, 30, 10, 0, 0),
                QtyOK = 100m,
                Status = WorkOrderStepStatus.Completed
            });
        await context.SaveChangesAsync();

        var metrics = await new OeeService(context).GetWorkCenterOeeAsync(
            workCenter.Id,
            new DateTime(2026, 7, 1),
            new DateTime(2026, 7, 1, 23, 59, 59));

        Assert.Equal(workCenter.Id, metrics.WorkCenterId);
        Assert.Equal("WC-01", metrics.WorkCenterCode);
        Assert.Equal("Assembly", metrics.WorkCenterName);
        Assert.Equal(25m, metrics.Availability);
        Assert.Equal(83.3m, metrics.Performance);
        Assert.Equal(90m, metrics.Quality);
        Assert.Equal(18.7m, metrics.Oee);
        Assert.Equal("danger", metrics.StatusColor);
    }

    [Fact]
    public async Task GetAllWorkCentersOeeAsync_ReturnsOnlyActiveCentersAndAssignsThresholdColors()
    {
        await using var context = CreateContext();
        context.WorkCenters.AddRange(
            new WorkCenter { Id = 1, Code = "WC-S", Name = "Success", IsActive = true },
            new WorkCenter { Id = 2, Code = "WC-W", Name = "Warning", IsActive = true },
            new WorkCenter { Id = 3, Code = "WC-I", Name = "Inactive", IsActive = false });
        context.WorkOrders.AddRange(
            new WorkOrder { Id = 1, Code = "WO-S", ProductId = 1, DueDate = DateTime.UtcNow, BomVersion = "V1", RoutingVersion = "V1" },
            new WorkOrder { Id = 2, Code = "WO-W", ProductId = 2, DueDate = DateTime.UtcNow, BomVersion = "V1", RoutingVersion = "V1" },
            new WorkOrder { Id = 3, Code = "WO-I", ProductId = 3, DueDate = DateTime.UtcNow, BomVersion = "V1", RoutingVersion = "V1" });
        context.Routings.AddRange(
            CreateRouting(1, 1, 1),
            CreateRouting(2, 2, 2),
            CreateRouting(3, 3, 3));
        context.WorkOrderSteps.AddRange(
            CreateCompletedStep(1, 1, 1, 480, 432, 48),
            CreateCompletedStep(2, 2, 2, 480, 336, 144),
            CreateCompletedStep(3, 3, 3, 480, 100, 0));
        await context.SaveChangesAsync();

        var metrics = (await new OeeService(context).GetAllWorkCentersOeeAsync(
            new DateTime(2026, 7, 1),
            new DateTime(2026, 7, 1, 23, 59, 59))).OrderBy(item => item.WorkCenterId).ToArray();

        Assert.Collection(metrics,
            success =>
            {
                Assert.Equal(90m, success.Oee);
                Assert.Equal("success", success.StatusColor);
            },
            warning =>
            {
                Assert.Equal(70m, warning.Oee);
                Assert.Equal("warning", warning.StatusColor);
            });
    }

    [Fact]
    public async Task GetInventoryAgingAnalyticsAsync_GroupsAvailableInventoryValueByManufactureAge()
    {
        await using var context = CreateContext();
        var today = DateTime.UtcNow.Date;
        context.Lots.AddRange(
            new Lot { Id = 1, ProductId = 1, LotNo = "L-30", ManufactureDate = today.AddDays(-30), UnitPrice = 2m },
            new Lot { Id = 2, ProductId = 1, LotNo = "L-60", ManufactureDate = today.AddDays(-60), UnitPrice = 3m },
            new Lot { Id = 3, ProductId = 1, LotNo = "L-90", ManufactureDate = today.AddDays(-90), UnitPrice = 4m },
            new Lot { Id = 4, ProductId = 1, LotNo = "L-91", ManufactureDate = today.AddDays(-91), UnitPrice = 5m });
        context.StockBalances.AddRange(
            new StockBalance { ProductId = 1, LotId = 1, LocationId = 1, QtyAvailable = 10m },
            new StockBalance { ProductId = 1, LotId = 2, LocationId = 1, QtyAvailable = 10m },
            new StockBalance { ProductId = 1, LotId = 3, LocationId = 1, QtyAvailable = 10m },
            new StockBalance { ProductId = 1, LotId = 4, LocationId = 1, QtyAvailable = 10m });
        await context.SaveChangesAsync();

        var aging = await new OeeService(context).GetInventoryAgingAnalyticsAsync();

        Assert.Equal(20m, aging.LessThan30Days);
        Assert.Equal(30m, aging.Days30To60);
        Assert.Equal(40m, aging.Days60To90);
        Assert.Equal(50m, aging.MoreThan90Days);
    }

    [Fact]
    public async Task GetProductionProgressAnalyticsAsync_SumsDailyOutputForInProgressOrdersWithoutCountingStepOutput()
    {
        await using var context = CreateContext();
        context.WorkOrders.AddRange(
            new WorkOrder
            {
                Id = 1,
                Code = "WO-ACTIVE",
                ProductId = 1,
                Qty = 20m,
                DueDate = new DateTime(2026, 8, 1),
                Status = WorkOrderStatus.InProgress,
                DailyProductionLogs =
                {
                    new DailyProductionLog
                    {
                        Date = new DateTime(2026, 7, 29),
                        QtyProduced = 4m
                    },
                    new DailyProductionLog
                    {
                        Date = new DateTime(2026, 7, 30),
                        QtyProduced = 6m
                    }
                },
                Steps =
                {
                    new WorkOrderStep
                    {
                        StepNumber = 10,
                        StepName = "Cut",
                        WorkCenterId = 1,
                        QtyOK = 10m,
                        Status = WorkOrderStepStatus.Completed
                    },
                    new WorkOrderStep
                    {
                        StepNumber = 20,
                        StepName = "Pack",
                        WorkCenterId = 1,
                        QtyOK = 10m,
                        Status = WorkOrderStepStatus.Completed
                    }
                }
            },
            new WorkOrder
            {
                Id = 2,
                Code = "WO-APPROVED",
                ProductId = 1,
                Qty = 15m,
                DueDate = new DateTime(2026, 8, 2),
                Status = WorkOrderStatus.Approved,
                DailyProductionLogs =
                {
                    new DailyProductionLog
                    {
                        Date = new DateTime(2026, 7, 30),
                        QtyProduced = 5m
                    }
                }
            },
            new WorkOrder
            {
                Id = 3,
                Code = "WO-COMPLETE",
                ProductId = 1,
                Qty = 30m,
                DueDate = new DateTime(2026, 8, 3),
                Status = WorkOrderStatus.Completed,
                DailyProductionLogs =
                {
                    new DailyProductionLog
                    {
                        Date = new DateTime(2026, 7, 30),
                        QtyProduced = 30m
                    }
                }
            });
        await context.SaveChangesAsync();

        var progress = (await new OeeService(context)
            .GetProductionProgressAnalyticsAsync()).ToArray();

        var active = Assert.Single(progress);
        Assert.Equal(1, active.WorkOrderId);
        Assert.Equal("WO-ACTIVE", active.WorkOrderCode);
        Assert.Equal(20m, active.PlannedQuantity);
        Assert.Equal(10m, active.ActualProducedQuantity);
    }

    private static Routing CreateRouting(
        int routingId,
        int productId,
        int workCenterId,
        decimal standardTimeMinutes = 1m) => new()
    {
        Id = routingId,
        ProductId = productId,
        Name = $"Routing {routingId}",
        Version = "V1",
        Steps =
        {
            new RoutingStep
            {
                StepNumber = 10,
                StepName = "Step 10",
                WorkCenterId = workCenterId,
                StandardTimeMinutes = standardTimeMinutes
            }
        }
    };

    private static WorkOrderStep CreateCompletedStep(
        int workOrderId,
        int workCenterId,
        int sequence,
        int durationMinutes,
        decimal qtyOk,
        decimal qtyReject) => new()
    {
        WorkOrderId = workOrderId,
        StepNumber = 10,
        StepName = $"Step {sequence}",
        WorkCenterId = workCenterId,
        StartTime = new DateTime(2026, 7, 1, 8, 0, 0),
        EndTime = new DateTime(2026, 7, 1, 8, 0, 0).AddMinutes(durationMinutes),
        QtyOK = qtyOk,
        QtyReject = qtyReject,
        Status = WorkOrderStepStatus.Completed
    };

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"OeeService_{Guid.NewGuid()}")
            .Options;

        return new ApplicationDbContext(options);
    }
}

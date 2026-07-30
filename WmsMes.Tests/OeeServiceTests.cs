using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
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

        var metrics = await CreateService(context).GetWorkCenterOeeAsync(
            workCenter.Id,
            new DateTime(2026, 7, 1),
            new DateTime(2026, 7, 2));

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

        var metrics = await CreateService(context).GetWorkCenterOeeAsync(
            workCenter.Id,
            new DateTime(2026, 7, 1),
            new DateTime(2026, 7, 2));

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

        var metrics = (await CreateService(context).GetAllWorkCentersOeeAsync(
            new DateTime(2026, 7, 1),
            new DateTime(2026, 7, 2))).OrderBy(item => item.WorkCenterId).ToArray();

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
    public async Task GetWorkCenterOeeAsync_UsesFullyContainedHalfOpenPeriodAndRejectsInvalidStepRanges()
    {
        await using var context = CreateContext();
        var workCenter = new WorkCenter { Id = 1, Code = "WC-BOUNDARY", Name = "Boundary" };
        context.WorkCenters.Add(workCenter);
        context.WorkOrders.Add(new WorkOrder
        {
            Id = 1,
            Code = "WO-BOUNDARY",
            ProductId = 1,
            DueDate = new DateTime(2026, 7, 31),
            BomVersion = "V1",
            RoutingVersion = "V1"
        });
        context.Routings.Add(CreateRouting(1, 1, workCenter.Id));

        var start = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        var endExclusive = start.AddDays(1);
        context.WorkOrderSteps.AddRange(
            StepAt("lower-inclusive", start, start.AddMinutes(10), 10m, 0m),
            StepAt("below-lower", start.AddTicks(-1), start.AddMinutes(10), 100m, 0m),
            StepAt("upper-exclusive", endExclusive.AddMinutes(-10), endExclusive, 100m, 0m),
            StepAt("below-upper", endExclusive.AddMinutes(-10), endExclusive.AddTicks(-1), 0m, 10m),
            StepAt("invalid-range", start.AddHours(2), start.AddHours(1), 100m, 0m));
        await context.SaveChangesAsync();

        var metrics = await CreateService(context).GetWorkCenterOeeAsync(
            workCenter.Id,
            start,
            endExclusive);

        Assert.Equal(50m, metrics.Quality);

        WorkOrderStep StepAt(
            string name,
            DateTime stepStart,
            DateTime stepEnd,
            decimal quantityOk,
            decimal quantityReject) => new()
        {
            WorkOrderId = 1,
            StepNumber = 10,
            StepName = name,
            WorkCenterId = workCenter.Id,
            StartTime = stepStart,
            EndTime = stepEnd,
            QtyOK = quantityOk,
            QtyReject = quantityReject,
            Status = WorkOrderStepStatus.Completed
        };
    }

    [Theory]
    [InlineData("2026-07-01", "2026-07-01")]
    [InlineData("2026-07-02", "2026-07-01")]
    public async Task GetAllWorkCentersOeeAsync_WhenPeriodIsEmptyOrReversed_Throws(
        string startValue,
        string endExclusiveValue)
    {
        await using var context = CreateContext();
        var service = CreateService(context);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.GetAllWorkCentersOeeAsync(
                DateTime.Parse(startValue),
                DateTime.Parse(endExclusiveValue)));
    }

    [Fact]
    public async Task GetInventoryAgingAnalyticsAsync_At0030Vietnam_UsesRecordedCalendarDateAtExactBoundaries()
    {
        await using var context = CreateContext();
        var today = new DateTime(2026, 7, 31);
        context.Lots.AddRange(
            // ManufactureDate is a recorded business calendar date. Legacy/imported
            // time and Kind metadata must not change its aging bucket.
            new Lot { Id = 1, ProductId = 1, LotNo = "L-29", ManufactureDate = today.AddDays(-29).AddHours(23), UnitPrice = 1m },
            new Lot { Id = 2, ProductId = 1, LotNo = "L-30", ManufactureDate = DateTime.SpecifyKind(today.AddDays(-30).AddHours(23), DateTimeKind.Utc), UnitPrice = 2m },
            new Lot { Id = 3, ProductId = 1, LotNo = "L-59", ManufactureDate = DateTime.SpecifyKind(today.AddDays(-59).AddHours(8), DateTimeKind.Local), UnitPrice = 4m },
            new Lot { Id = 4, ProductId = 1, LotNo = "L-60", ManufactureDate = today.AddDays(-60).AddHours(12), UnitPrice = 8m },
            new Lot { Id = 5, ProductId = 1, LotNo = "L-90", ManufactureDate = today.AddDays(-90).AddHours(23), UnitPrice = 16m },
            new Lot { Id = 6, ProductId = 1, LotNo = "L-91", ManufactureDate = today.AddDays(-91).AddHours(1), UnitPrice = 32m },
            new Lot { Id = 7, ProductId = 1, LotNo = "L-UNKNOWN", ManufactureDate = null, UnitPrice = 64m },
            new Lot { Id = 8, ProductId = 1, LotNo = "L-FUTURE", ManufactureDate = today.AddDays(1), UnitPrice = 128m });
        context.StockBalances.AddRange(
            Enumerable.Range(1, 8).Select(id => new StockBalance
            {
                ProductId = 1,
                LotId = id,
                LocationId = 1,
                QtyAvailable = 1m
            }));
        await context.SaveChangesAsync();

        var clock = new FixedTimeProvider(
            new DateTimeOffset(2026, 7, 30, 17, 30, 0, TimeSpan.Zero));
        var aging = await new OeeService(context, clock, VietnamTimeZone())
            .GetInventoryAgingAnalyticsAsync();

        Assert.Equal(129m, aging.LessThan30Days);
        Assert.Equal(6m, aging.Days30To60);
        Assert.Equal(24m, aging.Days60To90);
        Assert.Equal(32m, aging.MoreThan90Days);
        Assert.Equal(64m, aging.UnknownAge);
        Assert.Equal(255m, aging.TotalValue);
    }

    [Fact]
    public async Task GetInventoryAgingAnalyticsAsync_UsesConfiguredBusinessDate()
    {
        await using var context = CreateContext();
        context.Lots.Add(new Lot
        {
            Id = 1,
            ProductId = 1,
            LotNo = "L-BUSINESS-DATE",
            ManufactureDate = new DateTime(2026, 7, 1),
            UnitPrice = 5m
        });
        context.StockBalances.Add(new StockBalance
        {
            ProductId = 1,
            LotId = 1,
            LocationId = 1,
            QtyAvailable = 1m
        });
        await context.SaveChangesAsync();
        var clock = new FixedTimeProvider(
            new DateTimeOffset(2026, 7, 30, 18, 30, 0, TimeSpan.Zero));

        var aging = await new OeeService(context, clock, VietnamTimeZone())
            .GetInventoryAgingAnalyticsAsync();

        Assert.Equal(0m, aging.LessThan30Days);
        Assert.Equal(5m, aging.Days30To60);
    }

    [Fact]
    public async Task GetProductionQualityAnalyticsAsync_ReturnsTodayOutputScrapRateAndSevenDayTrend()
    {
        await using var context = CreateContext();
        var workCenter = new WorkCenter { Id = 1, Code = "WC-QUALITY", Name = "Quality" };
        var order = new WorkOrder
        {
            Id = 1,
            Code = "WO-QUALITY",
            ProductId = 1,
            DueDate = new DateTime(2026, 8, 1),
            BomVersion = "V1",
            RoutingVersion = "V1",
            Status = WorkOrderStatus.InProgress,
            DailyProductionLogs =
            {
                new DailyProductionLog { Date = new DateTime(2026, 7, 30), QtyProduced = 99m },
                new DailyProductionLog { Date = new DateTime(2026, 7, 31), QtyProduced = 5m },
                new DailyProductionLog { Date = new DateTime(2026, 7, 31), QtyProduced = 7m }
            }
        };
        context.AddRange(workCenter, order);
        context.WorkOrderSteps.AddRange(
            QualityStep("2026-07-30", new DateTime(2026, 7, 30, 1, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 7, 30, 2, 0, 0, DateTimeKind.Utc), 8m, 2m),
            QualityStep("2026-07-31", new DateTime(2026, 7, 31, 1, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 7, 31, 2, 0, 0, DateTimeKind.Utc), 9m, 1m),
            QualityStep("upper-exclusive", new DateTime(2026, 7, 31, 16, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 7, 31, 17, 0, 0, DateTimeKind.Utc), 0m, 100m));
        await context.SaveChangesAsync();
        var clock = new FixedTimeProvider(
            new DateTimeOffset(2026, 7, 30, 18, 30, 0, TimeSpan.Zero));
        var service = new OeeService(context, clock, VietnamTimeZone());

        var quality = await service.GetProductionQualityAnalyticsAsync(
            new DateTime(2026, 7, 24, 17, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 7, 31, 17, 0, 0, DateTimeKind.Utc));

        Assert.Equal(12m, quality.TodayProductionOutput);
        Assert.Equal(15m, quality.ScrapRate);
        Assert.Equal(7, quality.DailyTrend.Count);
        var today = quality.DailyTrend[^1];
        Assert.Equal("2026-07-31", today.BusinessDate);
        Assert.Equal(1m, today.ScrapQuantity);
        Assert.Equal(90m, today.QualityRate);

        WorkOrderStep QualityStep(
            string name,
            DateTime start,
            DateTime end,
            decimal quantityOk,
            decimal quantityReject) => new()
        {
            WorkOrderId = order.Id,
            StepNumber = 10,
            StepName = name,
            WorkCenterId = workCenter.Id,
            StartTime = start,
            EndTime = end,
            QtyOK = quantityOk,
            QtyReject = quantityReject,
            Status = WorkOrderStepStatus.Completed
        };
    }

    [Fact]
    public async Task GetAllWorkCentersOeeAsync_UsesConstantNumberOfRelationalQueries()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var commands = new List<string>();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .LogTo(commands.Add, [RelationalEventId.CommandExecuted], LogLevel.Information)
            .Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        context.WorkCenters.AddRange(
            new WorkCenter { Id = 1, Code = "WC-01", Name = "One", IsActive = true },
            new WorkCenter { Id = 2, Code = "WC-02", Name = "Two", IsActive = true });
        var uom = new UnitOfMeasure { Id = 1, Code = "EA-OEE", Name = "Each" };
        context.Products.AddRange(
            new Product { Id = 1, Code = "P-OEE-01", Name = "One", BaseUom = uom },
            new Product { Id = 2, Code = "P-OEE-02", Name = "Two", BaseUom = uom });
        context.WorkOrders.AddRange(
            new WorkOrder { Id = 1, Code = "WO-01", ProductId = 1, DueDate = DateTime.UtcNow, BomVersion = "V1", RoutingVersion = "V1" },
            new WorkOrder { Id = 2, Code = "WO-02", ProductId = 2, DueDate = DateTime.UtcNow, BomVersion = "V1", RoutingVersion = "V1" });
        context.Routings.AddRange(
            CreateRouting(1, 1, 1),
            CreateRouting(2, 2, 2));
        context.WorkOrderSteps.AddRange(
            CreateCompletedStep(1, 1, 1, 60, 10, 0),
            CreateCompletedStep(2, 2, 2, 60, 10, 0));
        await context.SaveChangesAsync();
        commands.Clear();

        var metrics = (await CreateService(context).GetAllWorkCentersOeeAsync(
            new DateTime(2026, 7, 1),
            new DateTime(2026, 7, 2))).ToArray();

        Assert.Equal(2, metrics.Length);
        Assert.InRange(commands.Count, 1, 3);
    }

    [Fact]
    public void Model_DefinesCompositeIndexForOeeReportingPredicate()
    {
        using var context = CreateContext();
        var entityType = context.Model.FindEntityType(typeof(WorkOrderStep));
        Assert.NotNull(entityType);

        var index = Assert.Single(entityType!.GetIndexes().Where(candidate =>
            candidate.Properties.Select(property => property.Name).SequenceEqual(
                new[]
                {
                    nameof(WorkOrderStep.WorkCenterId),
                    nameof(WorkOrderStep.Status),
                    nameof(WorkOrderStep.StartTime),
                    nameof(WorkOrderStep.EndTime)
                })));

        Assert.Equal("IX_WorkOrderSteps_OeeReporting", index.GetDatabaseName());
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

        var progress = (await CreateService(context)
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

    private static OeeService CreateService(ApplicationDbContext context) =>
        new(context, TimeProvider.System, TimeZoneInfo.Utc);

    private static TimeZoneInfo VietnamTimeZone() =>
        TimeZoneInfo.CreateCustomTimeZone(
            "Asia/Ho_Chi_Minh",
            TimeSpan.FromHours(7),
            "Vietnam",
            "Vietnam");

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}

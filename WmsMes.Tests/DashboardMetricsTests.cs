using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WmsMes.Web.Controllers;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Domain.Enums;
using WmsMes.Web.Services;
using WmsMes.Web.ViewModels;

namespace WmsMes.Tests;

public class DashboardMetricsTests
{
    [Fact]
    public void BusinessTimeZoneResolver_ResolvesVietnamIanaId()
    {
        var timeZone = BusinessTimeZoneResolver.Resolve("Asia/Ho_Chi_Minh");

        Assert.Equal(TimeSpan.FromHours(7), timeZone.BaseUtcOffset);
    }

    [Fact]
    public void HomeController_AcceptsInjectableClockAndBusinessTimeZone()
    {
        var constructor = Assert.Single(typeof(HomeController).GetConstructors());
        var parameterTypes = constructor.GetParameters().Select(parameter => parameter.ParameterType);

        Assert.Contains(typeof(TimeProvider), parameterTypes);
        Assert.Contains(typeof(TimeZoneInfo), parameterTypes);
    }

    [Fact]
    public async Task Metrics_GroupsUtcFinalStepEndTimesByBusinessDateAcrossMidnight()
    {
        await using var context = new ApplicationDbContext(Options($"Dashboard_TimeZone_{Guid.NewGuid()}"));
        var businessTimeZone = TimeZoneInfo.CreateCustomTimeZone(
            "Asia/Ho_Chi_Minh",
            TimeSpan.FromHours(7),
            "Vietnam",
            "Vietnam");
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 7, 20, 18, 0, 0, TimeSpan.Zero));

        context.WorkOrders.AddRange(
            WorkOrder("WO-BEFORE-MIDNIGHT", 11m, new DateTime(2026, 7, 20), WorkOrderStatus.Completed,
                new WorkOrderStep
                {
                    StepNumber = 10,
                    QtyOK = 11m,
                    EndTime = new DateTime(2026, 7, 20, 16, 30, 0, DateTimeKind.Utc),
                    Status = WorkOrderStepStatus.Completed
                }),
            WorkOrder("WO-AFTER-MIDNIGHT", 42m, new DateTime(2026, 7, 21), WorkOrderStatus.Completed,
                new WorkOrderStep
                {
                    StepNumber = 10,
                    QtyOK = 42m,
                    EndTime = new DateTime(2026, 7, 20, 17, 30, 0, DateTimeKind.Utc),
                    Status = WorkOrderStepStatus.Completed
                }));
        await context.SaveChangesAsync();

        var result = await Controller(context, clock, businessTimeZone).Metrics();

        var metrics = Assert.IsType<DashboardViewModel>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal("21/07", metrics.DailyLabels[^1]);
        Assert.Equal(11m, metrics.DailyActualOutput[^2]);
        Assert.Equal(42m, metrics.DailyActualOutput[^1]);
    }

    [Fact]
    public async Task Metrics_SqliteQueriesTranslateAndProjectOnlyDashboardFields()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var businessTimeZone = VietnamTimeZone();
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 7, 20, 18, 0, 0, TimeSpan.Zero));
        var commands = new List<string>();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .LogTo(commands.Add, [RelationalEventId.CommandExecuted], LogLevel.Information)
            .Options;
        await using var context = new ApplicationDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var uom = new UnitOfMeasure { Code = "EA-SQLITE", Name = "Each" };
        var product = new Product { Code = "P-SQLITE", Name = "SQLite product", BaseUom = uom };
        var location = new Location
        {
            Code = "SQLITE-01",
            Name = "SQLite location",
            Zone = new Zone
            {
                Code = "SQLITE-ZONE",
                Name = "SQLite Zone",
                Warehouse = new Warehouse { Code = "SQLITE-WH", Name = "SQLite warehouse" }
            }
        };
        var lot = new Lot { LotNo = "LOT-SQLITE", Product = product };
        var workCenter = new WorkCenter { Code = "WC-SQLITE", Name = "SQLite work center" };
        var workOrder = WorkOrder(
            "WO-SQLITE",
            10m,
            new DateTime(2026, 7, 21),
            WorkOrderStatus.Completed,
            new WorkOrderStep
            {
                StepNumber = 10,
                StepName = "Final",
                WorkCenter = workCenter,
                QtyOK = 8m,
                QtyReject = 2m,
                EndTime = new DateTime(2026, 7, 20, 17, 30, 0, DateTimeKind.Utc),
                Status = WorkOrderStepStatus.Completed
            });
        workOrder.Product = product;
        var historicalWorkOrder = WorkOrder(
            "WO-SQLITE-HISTORICAL",
            999m,
            new DateTime(2020, 1, 1),
            WorkOrderStatus.Completed,
            new WorkOrderStep
            {
                StepNumber = 10,
                StepName = "Historical final",
                WorkCenter = workCenter,
                QtyOK = 999m,
                EndTime = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                Status = WorkOrderStepStatus.Completed
            });
        historicalWorkOrder.Product = product;
        var balance = new StockBalance
        {
            Product = product,
            Lot = lot,
            Location = location,
            QtyAvailable = 1.25m,
            QtyReserved = 2.5m,
            QtyOnHold = 3.75m
        };
        context.AddRange(
            workOrder,
            historicalWorkOrder,
            balance,
            new QCInspection
            {
                WorkOrder = workOrder,
                Lot = lot,
                InspectorId = "sqlite-test",
                Result = QCResult.PASS
            });
        await context.SaveChangesAsync();
        commands.Clear();

        var result = await Controller(context, clock, businessTimeZone).Metrics();

        var metrics = Assert.IsType<DashboardViewModel>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(7.5m, metrics.InventoryVolume);
        Assert.Equal(1, metrics.LowStockAlertCount);
        Assert.Equal(1, metrics.PassedQcCount);
        Assert.Equal(1, metrics.HoldQcCount);
        Assert.Equal(["SQLite Zone"], metrics.ZoneLabels);
        Assert.Equal([7.5m], metrics.ZoneQuantities);
        Assert.Equal(10m, metrics.DailyPlannedOutput[^1]);
        Assert.Equal(8m, metrics.DailyActualOutput[^1]);
        Assert.NotEmpty(commands);
        var executedSql = string.Join(Environment.NewLine, commands);
        Assert.Contains("SUM(", executedSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GROUP BY", executedSql, StringComparison.OrdinalIgnoreCase);
        var plannedCommand = Assert.Single(commands.Where(command => command.Contains("\"DueDate\"", StringComparison.Ordinal)));
        Assert.Contains("WHERE", plannedCommand, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(">=", plannedCommand, StringComparison.Ordinal);
        Assert.Contains("<", plannedCommand, StringComparison.Ordinal);
        var actualCommand = Assert.Single(commands.Where(command => command.Contains("\"EndTime\"", StringComparison.Ordinal)));
        Assert.Contains("WHERE", actualCommand, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(">=", actualCommand, StringComparison.Ordinal);
        Assert.Contains("<", actualCommand, StringComparison.Ordinal);
        Assert.DoesNotContain(commands, command =>
            command.Contains("\"Status\"", StringComparison.Ordinal)
            && command.Contains("\"DueDate\"", StringComparison.Ordinal));
        Assert.DoesNotContain(commands, command => command.Contains("\"BomVersion\"", StringComparison.Ordinal));
        Assert.DoesNotContain(commands, command => command.Contains("\"RoutingVersion\"", StringComparison.Ordinal));
        Assert.DoesNotContain(commands, command => command.Contains("\"StepName\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Metrics_PreservesExactDecimalInventoryAggregatesOutsideSqlite()
    {
        await using var context = new ApplicationDbContext(Options($"Dashboard_Precision_{Guid.NewGuid()}"));
        var location = new Location
        {
            Code = "PRECISION-01",
            Name = "Precision location",
            Zone = new Zone { Code = "PRECISION", Name = "Precision Zone" }
        };
        var first = Balance(123456789012345.67m, location);
        first.QtyReserved = 0.11m;
        var second = Balance(0.10m, location);
        context.StockBalances.AddRange(first, second);
        await context.SaveChangesAsync();
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 7, 20, 18, 0, 0, TimeSpan.Zero));

        var result = await Controller(context, clock, VietnamTimeZone()).Metrics();

        var metrics = Assert.IsType<DashboardViewModel>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(123456789012345.88m, metrics.InventoryVolume);
        Assert.Equal([123456789012345.88m], metrics.ZoneQuantities);
    }

    [Fact]
    public void Metrics_SqlServerFinalStepAggregate_AvoidsAggregateOverSubquery()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=DashboardSqlShape;Trusted_Connection=True")
            .Options;
        using var context = new ApplicationDbContext(options);
        var finalSteps = context.WorkOrderSteps
            .AsNoTracking()
            .Where(step => !context.WorkOrderSteps.Any(candidate =>
                candidate.WorkOrderId == step.WorkOrderId && candidate.StepNumber > step.StepNumber));
        var sql = finalSteps
            .GroupBy(_ => 1)
            .Select(group => new
            {
                AcceptedQuantity = group.Sum(step => step.QtyOK),
                RejectedQuantity = group.Sum(step => step.QtyReject)
            })
            .ToQueryString();
        var controllerSource = File.ReadAllText(Path.Combine(ProjectRoot(), "Controllers", "HomeController.cs"));

        Assert.Contains("NOT EXISTS", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SUM(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Matches(@"SUM\(\[[^]]+\]\.\[QtyOK\]\)", sql);
        Assert.Matches(@"SUM\(\[[^]]+\]\.\[QtyReject\]\)", sql);
        Assert.DoesNotContain("TOP(1)", sql.Replace(" ", string.Empty), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SUM((SELECT", sql.ReplaceLineEndings(" "), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("_context.WorkOrderSteps", controllerSource, StringComparison.Ordinal);
        Assert.Contains("candidate.StepNumber > step.StepNumber", controllerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("group.Sum(order => order.FinalAcceptedQuantity", controllerSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Metrics_CalculatesOeeAlertsDailyOutputAndZoneInventory()
    {
        await using var context = new ApplicationDbContext(Options($"Dashboard_{Guid.NewGuid()}"));
        var today = new DateTime(2026, 1, 15);
        var finishedGoods = new Zone { Code = "FG", Name = "Finished Goods" };
        var rawMaterials = new Zone { Code = "RM", Name = "Raw Materials" };
        var quarantineZone = new Zone { Code = "QUAR", Name = "Quarantine" };
        var finishedLocation = new Location { Code = "FG-01", Name = "FG-01", Zone = finishedGoods };
        var finishedHoldingLocation = new Location { Code = "FG-02", Name = "FG-02", Zone = finishedGoods };
        var rawLocation = new Location { Code = "RM-01", Name = "RM-01", Zone = rawMaterials };
        var quarantineLocation = new Location { Code = QcService.QuarantineLocationCode, Name = "Quarantine", Zone = quarantineZone };
        var completedOrder = WorkOrder("WO-COMPLETE", 100m, today, WorkOrderStatus.Completed,
            new WorkOrderStep { StepNumber = 10, QtyOK = 90m, QtyReject = 10m, EndTime = today.AddHours(8), Status = WorkOrderStepStatus.Completed });
        var heldLot = Lot("HELD");
        var quarantinedLot = Lot("QUARANTINED");
        var completedBalance = Balance(10m, finishedLocation);
        var rawBalance = Balance(11m, rawLocation);

        context.WorkOrders.AddRange(
            completedOrder,
            WorkOrder("WO-IN-PROGRESS", 50m, today.AddDays(-1), WorkOrderStatus.InProgress),
            WorkOrder("WO-DRAFT", 50m, today.AddDays(-2), WorkOrderStatus.Draft));
        context.StockBalances.AddRange(
            completedBalance,
            Balance(4m, finishedLocation),
            rawBalance,
            Balance(11m, finishedLocation, heldLot, qtyOnHold: 1m),
            Balance(11m, finishedHoldingLocation, heldLot, qtyOnHold: 2m),
            Balance(11m, quarantineLocation, quarantinedLot, qtyOnHold: 1m));
        context.QCInspections.AddRange(
            new QCInspection { WorkOrder = completedOrder, Lot = completedBalance.Lot, InspectorId = "qc-1", Result = QCResult.PASS },
            new QCInspection { WorkOrder = completedOrder, Lot = rawBalance.Lot, InspectorId = "qc-1", Result = QCResult.REJECT });
        await context.SaveChangesAsync();

        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 1, 15, 1, 0, 0, TimeSpan.Zero));
        var result = await Controller(context, clock, VietnamTimeZone()).Metrics();

        var metrics = Assert.IsType<DashboardViewModel>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(2, metrics.LowStockAlertCount);
        Assert.Equal(1, metrics.ActiveWorkOrders);
        Assert.Equal(1, metrics.PassedQcCount);
        Assert.Equal(1, metrics.HoldQcCount);
        Assert.Equal(1, metrics.QuarantineQcCount);
        Assert.Equal(66.7m, metrics.OeeAvailabilityPercent);
        Assert.Equal(50m, metrics.OeePerformancePercent);
        Assert.Equal(90m, metrics.OeeQualityPercent);
        Assert.Equal(30m, metrics.OverallOeePercent);
        Assert.Equal(Enumerable.Range(0, 7).Select(day => today.AddDays(day - 6).ToString("dd/MM")), metrics.DailyLabels);
        Assert.Equal(new[] { 0m, 0m, 0m, 0m, 50m, 50m, 100m }, metrics.DailyPlannedOutput);
        Assert.Equal(new[] { 0m, 0m, 0m, 0m, 0m, 0m, 90m }, metrics.DailyActualOutput);
        Assert.Equal(new[] { "Finished Goods", "Quarantine", "Raw Materials" }, metrics.ZoneLabels);
        Assert.Equal(new[] { 39m, 12m, 11m }, metrics.ZoneQuantities);
    }

    private static DbContextOptions<ApplicationDbContext> Options(string name) =>
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(name).Options;

    private static HomeController Controller(ApplicationDbContext context) =>
        Controller(
            context,
            TimeProvider.System,
            VietnamTimeZone());

    private static TimeZoneInfo VietnamTimeZone() =>
        TimeZoneInfo.CreateCustomTimeZone("Asia/Ho_Chi_Minh", TimeSpan.FromHours(7), "Vietnam", "Vietnam");

    private static string ProjectRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    private static HomeController Controller(
        ApplicationDbContext context,
        TimeProvider timeProvider,
        TimeZoneInfo businessTimeZone) =>
        new(
            NullLogger<HomeController>.Instance,
            context,
            timeProvider,
            businessTimeZone);

    private static WorkOrder WorkOrder(string code, decimal qty, DateTime dueDate, WorkOrderStatus status, params WorkOrderStep[] steps) =>
        new()
        {
            Code = code,
            Qty = qty,
            DueDate = dueDate,
            Status = status,
            BomVersion = "B1",
            RoutingVersion = "R1",
            Steps = steps
        };

    private static Lot Lot(string lotNo) =>
        new() { LotNo = lotNo, Product = new Product { Code = $"P-{lotNo}", Name = $"Product {lotNo}" } };

    private static StockBalance Balance(decimal qtyAvailable, Location location, Lot? lot = null, decimal qtyOnHold = 0m) =>
        new()
        {
            Product = new Product { Code = Guid.NewGuid().ToString(), Name = "Product" },
            Lot = lot ?? Lot(Guid.NewGuid().ToString()),
            Location = location,
            QtyAvailable = qtyAvailable,
            QtyOnHold = qtyOnHold
        };

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}

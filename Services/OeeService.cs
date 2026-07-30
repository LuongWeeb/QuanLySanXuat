using Microsoft.EntityFrameworkCore;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Domain.Enums;
using WmsMes.Web.DTOs;

namespace WmsMes.Web.Services;

public class OeeService : IOeeService
{
    private const decimal PlannedMinutesPerDay = 480m;
    private readonly ApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;
    private readonly TimeZoneInfo _businessTimeZone;

    public OeeService(
        ApplicationDbContext context,
        TimeProvider timeProvider,
        TimeZoneInfo businessTimeZone)
    {
        _context = context;
        _timeProvider = timeProvider;
        _businessTimeZone = businessTimeZone;
    }

    public async Task<OeeMetricsDto> GetWorkCenterOeeAsync(
        int workCenterId,
        DateTime startDate,
        DateTime endExclusive)
    {
        ValidatePeriod(startDate, endExclusive);
        var workCenter = await _context.WorkCenters
            .AsNoTracking()
            .FirstOrDefaultAsync(center => center.Id == workCenterId);

        if (workCenter is null)
        {
            return new OeeMetricsDto { WorkCenterId = workCenterId };
        }

        return (await LoadMetricsAsync([workCenter], startDate, endExclusive))[0];
    }

    public async Task<IEnumerable<OeeMetricsDto>> GetAllWorkCentersOeeAsync(
        DateTime startDate,
        DateTime endExclusive)
    {
        ValidatePeriod(startDate, endExclusive);
        var workCenters = await _context.WorkCenters
            .AsNoTracking()
            .Where(center => center.IsActive)
            .OrderBy(center => center.Code)
            .ThenBy(center => center.Id)
            .ToListAsync();

        if (workCenters.Count == 0)
        {
            return [];
        }

        return await LoadMetricsAsync(workCenters, startDate, endExclusive);
    }

    public async Task<InventoryAgingDto> GetInventoryAgingAnalyticsAsync()
    {
        var today = DateOnly.FromDateTime(GetBusinessDate());
        var balances = await _context.StockBalances
            .AsNoTracking()
            .Where(balance => balance.Lot != null)
            .Select(balance => new InventoryAgingRow(
                balance.Lot!.ManufactureDate,
                (balance.QtyAvailable + balance.QtyReserved + balance.QtyOnHold) *
                balance.Lot.UnitPrice))
            .ToListAsync();

        var aging = new InventoryAgingDto();
        foreach (var balance in balances)
        {
            var manufactureDate = balance.ManufactureDate;
            if (!manufactureDate.HasValue)
            {
                aging.UnknownAge += balance.InventoryValue;
                continue;
            }

            // ManufactureDate is a calendar date. Imported/legacy values may retain a
            // time or Kind, so age by the recorded date component without converting
            // it as an instant or relying on database DateTime.Kind round-tripping.
            var recordedDate = DateOnly.FromDateTime(manufactureDate.Value);
            var ageInDays = Math.Max(0, today.DayNumber - recordedDate.DayNumber);

            if (ageInDays < 30)
            {
                aging.LessThan30Days += balance.InventoryValue;
            }
            else if (ageInDays < 60)
            {
                aging.Days30To60 += balance.InventoryValue;
            }
            else if (ageInDays <= 90)
            {
                aging.Days60To90 += balance.InventoryValue;
            }
            else
            {
                aging.MoreThan90Days += balance.InventoryValue;
            }
        }

        return aging;
    }

    public async Task<ProductionQualityAnalyticsDto> GetProductionQualityAnalyticsAsync(
        DateTime startDate,
        DateTime endExclusive)
    {
        ValidatePeriod(startDate, endExclusive);
        var businessDate = GetBusinessDate();
        var todayProductionOutput = await _context.DailyProductionLogs
            .AsNoTracking()
            .Where(log => log.Date == businessDate)
            .SumAsync(log => (decimal?)log.QtyProduced) ?? 0m;
        var completedSteps = await _context.WorkOrderSteps
            .AsNoTracking()
            .Where(step =>
                step.Status == WorkOrderStepStatus.Completed &&
                step.StartTime.HasValue &&
                step.EndTime.HasValue &&
                step.StartTime.Value >= startDate &&
                step.EndTime.Value < endExclusive &&
                step.EndTime.Value >= step.StartTime.Value)
            .Select(step => new QualityStepRow(
                step.EndTime!.Value,
                step.QtyOK,
                step.QtyReject,
                step.QtyRework))
            .ToListAsync();

        var totalProduced = completedSteps.Sum(step =>
            step.QtyOK + step.QtyReject + step.QtyRework);
        var totalReject = completedSteps.Sum(step => step.QtyReject);
        var startBusinessDate = ToBusinessDate(startDate);
        var endBusinessDateExclusive = ToBusinessDate(endExclusive);
        var dailyTrend = Enumerable
            .Range(0, (endBusinessDateExclusive - startBusinessDate).Days)
            .Select(offset =>
            {
                var date = startBusinessDate.AddDays(offset);
                var daySteps = completedSteps
                    .Where(step => ToBusinessDate(step.EndTime) == date)
                    .ToArray();
                var dayProduced = daySteps.Sum(step =>
                    step.QtyOK + step.QtyReject + step.QtyRework);
                var dayOk = daySteps.Sum(step => step.QtyOK);
                return new ProductionQualityTrendPointDto
                {
                    BusinessDate = date.ToString("yyyy-MM-dd"),
                    ScrapQuantity = daySteps.Sum(step => step.QtyReject),
                    QualityRate = dayProduced <= 0m
                        ? 0m
                        : RoundPercentage(dayOk * 100m / dayProduced)
                };
            })
            .ToList();

        return new ProductionQualityAnalyticsDto
        {
            TodayProductionOutput = todayProductionOutput,
            ScrapRate = totalProduced <= 0m
                ? 0m
                : RoundPercentage(totalReject * 100m / totalProduced),
            DailyTrend = dailyTrend
        };
    }

    public async Task<IEnumerable<ProductionProgressDto>> GetProductionProgressAnalyticsAsync()
    {
        return await _context.WorkOrders
            .AsNoTracking()
            .Where(order => order.Status == WorkOrderStatus.InProgress)
            .OrderBy(order => order.DueDate)
            .ThenBy(order => order.Code)
            .Select(order => new ProductionProgressDto
            {
                WorkOrderId = order.Id,
                WorkOrderCode = order.Code,
                PlannedQuantity = order.Qty,
                ActualProducedQuantity = order.DailyProductionLogs
                    .Sum(log => (decimal?)log.QtyProduced) ?? 0m
            })
            .ToListAsync();
    }

    private async Task<IReadOnlyList<OeeMetricsDto>> LoadMetricsAsync(
        IReadOnlyList<WorkCenter> workCenters,
        DateTime startDate,
        DateTime endExclusive)
    {
        var workCenterIds = workCenters.Select(center => center.Id).ToArray();
        var completedSteps = await _context.WorkOrderSteps
            .AsNoTracking()
            .Where(step => workCenterIds.Contains(step.WorkCenterId) &&
                step.Status == WorkOrderStepStatus.Completed &&
                step.StartTime.HasValue &&
                step.EndTime.HasValue &&
                step.StartTime.Value >= startDate &&
                step.EndTime.Value < endExclusive &&
                step.EndTime.Value >= step.StartTime.Value &&
                step.WorkOrder != null)
            .Select(step => new CompletedStepRow(
                step.WorkCenterId,
                step.StartTime!.Value,
                step.EndTime!.Value,
                step.QtyOK,
                step.QtyReject,
                step.QtyRework,
                step.WorkOrder!.ProductId,
                step.WorkOrder.RoutingVersion,
                step.StepNumber))
            .ToListAsync();

        var productIds = completedSteps
            .Select(step => step.ProductId)
            .Distinct()
            .ToArray();
        var routingSteps = productIds.Length == 0
            ? []
            : await _context.RoutingSteps
                .AsNoTracking()
                .Where(step => workCenterIds.Contains(step.WorkCenterId) &&
                    productIds.Contains(step.Routing!.ProductId))
                .Select(step => new RoutingStandardRow(
                    step.Id,
                    step.RoutingId,
                    step.WorkCenterId,
                    step.Routing!.ProductId,
                    step.Routing.Version,
                    step.StepNumber,
                    step.StandardTimeMinutes))
                .ToListAsync();
        var standardMinutes = routingSteps
            .GroupBy(step => new StepKey(
                step.WorkCenterId,
                step.ProductId,
                step.RoutingVersion,
                step.StepNumber))
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(step => step.RoutingId)
                    .ThenByDescending(step => step.Id)
                    .First()
                    .StandardTimeMinutes);
        var plannedMinutes =
            (decimal)(endExclusive - startDate).TotalDays * PlannedMinutesPerDay;

        return workCenters.Select(workCenter =>
        {
            var centerSteps = completedSteps
                .Where(step => step.WorkCenterId == workCenter.Id)
                .ToArray();
            var actualOperatingMinutes = centerSteps.Sum(step =>
                (decimal)(step.EndTime - step.StartTime).TotalMinutes);
            var totalProduced = centerSteps.Sum(step =>
                step.QtyOK + step.QtyReject + step.QtyRework);
            var totalOk = centerSteps.Sum(step => step.QtyOK);
            var idealOperatingMinutes = centerSteps.Sum(step =>
                (step.QtyOK + step.QtyReject + step.QtyRework) *
                standardMinutes.GetValueOrDefault(new StepKey(
                    step.WorkCenterId,
                    step.ProductId,
                    step.RoutingVersion,
                    step.StepNumber)));

            var availability = ClampToPercentage(
                actualOperatingMinutes * 100m / plannedMinutes);
            var performance = actualOperatingMinutes <= 0m
                ? 0m
                : ClampToPercentage(idealOperatingMinutes * 100m / actualOperatingMinutes);
            var quality = totalProduced <= 0m
                ? 100m
                : ClampToPercentage(totalOk * 100m / totalProduced);
            var oee = RoundPercentage(availability * performance * quality / 10_000m);

            return new OeeMetricsDto
            {
                WorkCenterId = workCenter.Id,
                WorkCenterCode = workCenter.Code,
                WorkCenterName = workCenter.Name,
                Availability = RoundPercentage(availability),
                Performance = RoundPercentage(performance),
                Quality = RoundPercentage(quality),
                Oee = oee,
                StatusColor = GetStatusColor(oee)
            };
        }).ToArray();
    }

    private static decimal ClampToPercentage(decimal value) => Math.Min(100m, Math.Max(0m, value));

    private static decimal RoundPercentage(decimal value) =>
        Math.Round(value, 1, MidpointRounding.AwayFromZero);

    private static string GetStatusColor(decimal oee) => oee switch
    {
        >= 85m => "success",
        >= 65m => "warning",
        _ => "danger"
    };

    private static void ValidatePeriod(DateTime startDate, DateTime endExclusive)
    {
        if (startDate >= endExclusive)
        {
            throw new ArgumentException(
                "The reporting period must have a start before its exclusive end.",
                nameof(endExclusive));
        }
    }

    private DateTime GetBusinessDate() =>
        TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), _businessTimeZone).Date;

    private DateTime ToBusinessDate(DateTime utcDateTime) =>
        TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc),
            _businessTimeZone).Date;

    private sealed record CompletedStepRow(
        int WorkCenterId,
        DateTime StartTime,
        DateTime EndTime,
        decimal QtyOK,
        decimal QtyReject,
        decimal QtyRework,
        int ProductId,
        string RoutingVersion,
        int StepNumber);

    private sealed record RoutingStandardRow(
        int Id,
        int RoutingId,
        int WorkCenterId,
        int ProductId,
        string RoutingVersion,
        int StepNumber,
        decimal StandardTimeMinutes);

    private sealed record InventoryAgingRow(
        DateTime? ManufactureDate,
        decimal InventoryValue);

    private sealed record QualityStepRow(
        DateTime EndTime,
        decimal QtyOK,
        decimal QtyReject,
        decimal QtyRework);

    private readonly record struct StepKey(
        int WorkCenterId,
        int ProductId,
        string RoutingVersion,
        int StepNumber);
}

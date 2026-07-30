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

    public OeeService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<OeeMetricsDto> GetWorkCenterOeeAsync(
        int workCenterId,
        DateTime startDate,
        DateTime endDate)
    {
        var workCenter = await _context.WorkCenters
            .AsNoTracking()
            .FirstOrDefaultAsync(center => center.Id == workCenterId);

        if (workCenter is null)
        {
            return new OeeMetricsDto { WorkCenterId = workCenterId };
        }

        return await CalculateMetricsAsync(workCenter, startDate, endDate);
    }

    public async Task<IEnumerable<OeeMetricsDto>> GetAllWorkCentersOeeAsync(
        DateTime startDate,
        DateTime endDate)
    {
        var workCenters = await _context.WorkCenters
            .AsNoTracking()
            .Where(center => center.IsActive)
            .OrderBy(center => center.Code)
            .ThenBy(center => center.Id)
            .ToListAsync();

        var metrics = new List<OeeMetricsDto>(workCenters.Count);
        foreach (var workCenter in workCenters)
        {
            metrics.Add(await CalculateMetricsAsync(workCenter, startDate, endDate));
        }

        return metrics;
    }

    public async Task<InventoryAgingDto> GetInventoryAgingAnalyticsAsync()
    {
        var today = DateTime.UtcNow.Date;
        var balances = await _context.StockBalances
            .AsNoTracking()
            .Include(balance => balance.Lot)
            .Where(balance => balance.Lot != null)
            .ToListAsync();

        var aging = new InventoryAgingDto();
        foreach (var balance in balances)
        {
            var manufactureDate = balance.Lot!.ManufactureDate;
            if (!manufactureDate.HasValue)
            {
                continue;
            }

            var ageInDays = Math.Max(0, (today - manufactureDate.Value.Date).Days);
            var inventoryValue = (balance.QtyAvailable + balance.QtyReserved + balance.QtyOnHold) *
                balance.Lot.UnitPrice;

            if (ageInDays <= 30)
            {
                aging.LessThan30Days += inventoryValue;
            }
            else if (ageInDays <= 60)
            {
                aging.Days30To60 += inventoryValue;
            }
            else if (ageInDays <= 90)
            {
                aging.Days60To90 += inventoryValue;
            }
            else
            {
                aging.MoreThan90Days += inventoryValue;
            }
        }

        return aging;
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

    private async Task<OeeMetricsDto> CalculateMetricsAsync(
        WorkCenter workCenter,
        DateTime startDate,
        DateTime endDate)
    {
        var completedSteps = await _context.WorkOrderSteps
            .AsNoTracking()
            .Include(step => step.WorkOrder)
            .Where(step => step.WorkCenterId == workCenter.Id &&
                step.Status == WorkOrderStepStatus.Completed &&
                step.StartTime.HasValue &&
                step.EndTime.HasValue &&
                step.EndTime >= startDate &&
                step.EndTime <= endDate)
            .ToListAsync();

        var actualOperatingMinutes = completedSteps.Sum(step =>
            (decimal)(step.EndTime!.Value - step.StartTime!.Value).TotalMinutes);
        var totalProduced = completedSteps.Sum(step => step.QtyOK + step.QtyReject + step.QtyRework);
        var totalOk = completedSteps.Sum(step => step.QtyOK);
        var plannedDays = Math.Max(1, (endDate.Date - startDate.Date).Days + 1);
        var plannedMinutes = plannedDays * PlannedMinutesPerDay;

        var standardMinutes = await GetStandardMinutesAsync(completedSteps, workCenter.Id);
        var idealOperatingMinutes = completedSteps.Sum(step =>
            (step.QtyOK + step.QtyReject + step.QtyRework) *
            standardMinutes.GetValueOrDefault(new StepKey(
                step.WorkOrder?.ProductId ?? 0,
                step.WorkOrder?.RoutingVersion ?? string.Empty,
                step.StepNumber)));

        var availability = ClampToPercentage(actualOperatingMinutes * 100m / plannedMinutes);
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
    }

    private async Task<Dictionary<StepKey, decimal>> GetStandardMinutesAsync(
        IReadOnlyCollection<WorkOrderStep> completedSteps,
        int workCenterId)
    {
        var productIds = completedSteps
            .Where(step => step.WorkOrder is not null)
            .Select(step => step.WorkOrder!.ProductId)
            .Distinct()
            .ToArray();
        if (productIds.Length == 0)
        {
            return [];
        }

        var routingSteps = await _context.RoutingSteps
            .AsNoTracking()
            .Where(step => step.WorkCenterId == workCenterId &&
                productIds.Contains(step.Routing!.ProductId))
            .Select(step => new
            {
                step.Id,
                step.StepNumber,
                step.StandardTimeMinutes,
                RoutingId = step.RoutingId,
                ProductId = step.Routing!.ProductId,
                RoutingVersion = step.Routing.Version
            })
            .ToListAsync();

        return routingSteps
            .GroupBy(step => new StepKey(step.ProductId, step.RoutingVersion, step.StepNumber))
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(step => step.RoutingId)
                    .ThenByDescending(step => step.Id)
                    .First()
                    .StandardTimeMinutes);
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

    private readonly record struct StepKey(int ProductId, string RoutingVersion, int StepNumber);
}

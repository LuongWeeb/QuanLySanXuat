using System.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Domain.Enums;
using WmsMes.Web.Hubs;

namespace WmsMes.Web.Services;

public class WorkOrderService : IWorkOrderService
{
    public const string FinishedGoodsQcLocationCode = "LOC-FG-01";
    private readonly ApplicationDbContext _context;
    private readonly IHubContext<ProductionHub>? _productionHub;

    public WorkOrderService(ApplicationDbContext context, IHubContext<ProductionHub>? productionHub = null)
    {
        _context = context;
        _productionHub = productionHub;
    }

    public async Task<WorkOrder?> GetByIdAsync(int id)
    {
        return await _context.WorkOrders
            .Include(w => w.Product)
            .Include(w => w.Steps)
            .FirstOrDefaultAsync(w => w.Id == id);
    }

    public async Task<bool> CreateWorkOrderAsync(WorkOrder workOrder)
    {
        _context.WorkOrders.Add(workOrder);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ApproveWorkOrderAsync(int workOrderId, string userId)
    {
        await using var transaction = await BeginTransactionIfSupportedAsync();
        try
        {
            var workOrder = await _context.WorkOrders
                .Include(w => w.Steps)
                .FirstOrDefaultAsync(w => w.Id == workOrderId);

            if (workOrder == null)
            {
                return false;
            }

            if (workOrder.Status != WorkOrderStatus.Draft && workOrder.Status != WorkOrderStatus.Pending)
            {
                return false;
            }

            if (_context.Database.IsRelational())
            {
                var claimed = await _context.WorkOrders
                    .Where(order => order.Id == workOrderId &&
                        (order.Status == WorkOrderStatus.Draft || order.Status == WorkOrderStatus.Pending))
                    .ExecuteUpdateAsync(setters => setters.SetProperty(order => order.Status, WorkOrderStatus.Approved));
                if (claimed == 0)
                {
                    await RollbackIfSupportedAsync(transaction);
                    return false;
                }
                workOrder.Status = WorkOrderStatus.Approved;
            }

            var bom = await _context.BOMs
                .Include(b => b.Items)
                .FirstOrDefaultAsync(b => b.ProductId == workOrder.ProductId && b.IsActive);
            if (bom == null)
            {
                throw new InvalidOperationException("Active BOM was not found for the work order product.");
            }

            var routing = await _context.Routings
                .Include(r => r.Steps)
                .FirstOrDefaultAsync(r => r.ProductId == workOrder.ProductId && r.IsActive);
            if (routing == null)
            {
                throw new InvalidOperationException("Active routing was not found for the work order product.");
            }

            var requirements = bom.Items
                .GroupBy(i => i.ComponentProductId)
                .Select(g => new
                {
                    ProductId = g.Key,
                    QtyRequired = g.Sum(i => workOrder.Qty * i.QtyPer * (1 + i.ScrapPercent / 100))
                })
                .ToList();

            foreach (var requirement in requirements)
            {
                var availableQuantities = await _context.StockBalances
                    .Where(sb => sb.ProductId == requirement.ProductId)
                    .Select(sb => sb.QtyAvailable)
                    .ToListAsync();
                var available = availableQuantities.Sum();

                if (available < requirement.QtyRequired)
                {
                    throw new InvalidOperationException($"Insufficient material for product {requirement.ProductId}.");
                }
            }

            foreach (var requirement in requirements)
            {
                var remaining = requirement.QtyRequired;
                var balances = await _context.StockBalances
                    .Include(sb => sb.Lot)
                    .Where(sb => sb.ProductId == requirement.ProductId && sb.QtyAvailable > 0)
                    .OrderBy(sb => sb.Lot!.ExpiryDate ?? DateTime.MaxValue)
                    .ThenBy(sb => sb.Lot!.ManufactureDate ?? DateTime.MinValue)
                    .ThenBy(sb => sb.LotId)
                    .ToListAsync();

                foreach (var balance in balances)
                {
                    if (remaining <= 0)
                    {
                        break;
                    }

                    var qtyToReserve = Math.Min(balance.QtyAvailable, remaining);
                    balance.QtyAvailable -= qtyToReserve;
                    balance.QtyReserved += qtyToReserve;
                    remaining -= qtyToReserve;

                    _context.MaterialReservations.Add(new MaterialReservation
                    {
                        WorkOrderId = workOrder.Id,
                        ProductId = requirement.ProductId,
                        LotId = balance.LotId,
                        LocationId = balance.LocationId,
                        QtyReserved = qtyToReserve
                    });
                }
            }

            if (!workOrder.Steps.Any())
            {
                foreach (var step in routing.Steps.OrderBy(s => s.StepNumber))
                {
                    workOrder.Steps.Add(new WorkOrderStep
                    {
                        StepNumber = step.StepNumber,
                        StepName = step.StepName,
                        WorkCenterId = step.WorkCenterId
                    });
                }
            }

            workOrder.BomVersion = bom.Version;
            workOrder.RoutingVersion = routing.Version;
            workOrder.Status = WorkOrderStatus.Approved;

            await _context.SaveChangesAsync();
            await CommitIfSupportedAsync(transaction);
            await NotifyProgressAsync();
            return true;
        }
        catch
        {
            await RollbackIfSupportedAsync(transaction);
            throw;
        }
    }

    public async Task<bool> StartStepAsync(int stepId)
    {
        var step = await _context.WorkOrderSteps
            .Include(s => s.WorkOrder)
            .FirstOrDefaultAsync(s => s.Id == stepId);
        if (step == null)
        {
            return false;
        }

        if (step.Status != WorkOrderStepStatus.Pending)
        {
            return false;
        }

        if (step.WorkOrder == null ||
            (step.WorkOrder.Status != WorkOrderStatus.Approved && step.WorkOrder.Status != WorkOrderStatus.InProgress))
        {
            throw new InvalidOperationException("Work order is not ready for production.");
        }

        var previousIncomplete = await _context.WorkOrderSteps.AnyAsync(s =>
            s.WorkOrderId == step.WorkOrderId &&
            s.StepNumber < step.StepNumber &&
            s.Status != WorkOrderStepStatus.Completed);
        if (previousIncomplete)
        {
            throw new InvalidOperationException("Previous routing steps must be completed before this step can start.");
        }

        step.StartTime = DateTime.UtcNow;
        step.Status = WorkOrderStepStatus.InProgress;
        step.WorkOrder.Status = WorkOrderStatus.InProgress;

        await _context.SaveChangesAsync();
        await NotifyProgressAsync();
        return true;
    }

    public async Task<bool> CompleteStepAsync(int stepId, decimal qtyOk, decimal qtyReject, decimal qtyRework)
    {
        var step = await _context.WorkOrderSteps
            .Include(s => s.WorkOrder)
            .FirstOrDefaultAsync(s => s.Id == stepId);
        if (step == null)
        {
            return false;
        }

        if (step.Status != WorkOrderStepStatus.InProgress)
        {
            return false;
        }

        step.QtyOK = qtyOk;
        step.QtyReject = qtyReject;
        step.QtyRework = qtyRework;
        step.EndTime = DateTime.UtcNow;
        step.Status = WorkOrderStepStatus.Completed;

        await _context.SaveChangesAsync();
        await NotifyProgressAsync();
        return true;
    }

    public async Task<bool> CompleteWorkOrderAsync(int workOrderId, string userId)
    {
        await using var transaction = await BeginTransactionIfSupportedAsync();
        try
        {
            var workOrder = await _context.WorkOrders
                .Include(w => w.Steps)
                    .ThenInclude(step => step.WorkCenter)
                .FirstOrDefaultAsync(w => w.Id == workOrderId);
            if (workOrder == null)
            {
                return false;
            }

            if (workOrder.Status != WorkOrderStatus.InProgress)
            {
                return false;
            }

            if (workOrder.Steps.Count == 0 || workOrder.Steps.Any(s => s.Status != WorkOrderStepStatus.Completed))
            {
                throw new InvalidOperationException("Cannot complete a work order before all routing steps are completed.");
            }

            var product = await _context.Products.FindAsync(workOrder.ProductId);
            if (product == null)
            {
                throw new InvalidOperationException("Work order product was not found.");
            }

            var finalQty = workOrder.Steps.OrderByDescending(s => s.StepNumber).First().QtyOK;
            var reservations = await _context.MaterialReservations
                .Include(reservation => reservation.Lot)
                .Where(reservation => reservation.WorkOrderId == workOrder.Id)
                .ToListAsync();
            var actualMaterialCost = reservations.Sum(reservation =>
                reservation.QtyReserved *
                (reservation.Lot?.UnitPrice
                    ?? throw new InvalidOperationException(
                        "The backflush valuation lot no longer exists.")));

            var activeRouting = await _context.Routings
                .AsNoTracking()
                .Include(routing => routing.Steps)
                .Where(routing => routing.ProductId == workOrder.ProductId && routing.IsActive)
                .OrderByDescending(routing => routing.Id)
                .FirstOrDefaultAsync();
            var actualOperationCost = 0m;
            foreach (var step in workOrder.Steps)
            {
                if (step.WorkCenter is null)
                {
                    continue;
                }

                var durationMinutes = step.StartTime.HasValue && step.EndTime.HasValue
                    ? (decimal)(step.EndTime.Value - step.StartTime.Value).TotalMinutes
                    : 0m;
                if (durationMinutes <= 0m)
                {
                    var standardTimeMinutes = activeRouting?.Steps
                        .Where(routingStep => routingStep.StepNumber == step.StepNumber)
                        .Select(routingStep => routingStep.StandardTimeMinutes)
                        .FirstOrDefault() ?? 0m;
                    durationMinutes = standardTimeMinutes > 0m
                        ? standardTimeMinutes
                        : 0m;
                }

                actualOperationCost += durationMinutes / 60m *
                    (step.WorkCenter.HourlyLaborRate + step.WorkCenter.HourlyMachineRate);
            }

            var totalActualCost = actualMaterialCost + actualOperationCost;
            var unitActualCost = finalQty > 0m
                ? Math.Round(totalActualCost / finalQty, 2, MidpointRounding.AwayFromZero)
                : 0m;
            var qcLocationId = await _context.Locations
                .Where(location => location.Code == FinishedGoodsQcLocationCode && location.IsActive)
                .Select(location => (int?)location.Id)
                .SingleOrDefaultAsync()
                ?? throw new InvalidOperationException($"Active QC inspection location {FinishedGoodsQcLocationCode} was not found.");
            var today = DateTime.Today;
            var prefix = $"{product.Code}-{today:yyyyMMdd}-";
            var existingCount = await _context.Lots.CountAsync(l => l.LotNo.StartsWith(prefix));
            var finishedLot = new Lot
            {
                LotNo = $"{prefix}{existingCount + 1:D4}",
                ProductId = workOrder.ProductId,
                ManufactureDate = DateTime.UtcNow,
                ExpiryDate = product.ShelfLifeDays.HasValue ? DateTime.UtcNow.AddDays(product.ShelfLifeDays.Value) : null,
                Qty = finalQty,
                UnitPrice = unitActualCost,
                WorkOrderId = workOrder.Id
            };

            _context.Lots.Add(finishedLot);
            await _context.SaveChangesAsync();

            var finishedBalance = new StockBalance
            {
                ProductId = workOrder.ProductId,
                LotId = finishedLot.Id,
                LocationId = qcLocationId,
                QtyAvailable = 0m,
                QtyOnHold = finalQty
            };
            _context.StockBalances.Add(finishedBalance);
            _context.StockTransactions.Add(new StockTransaction
            {
                Type = TransactionType.Receipt,
                ProductId = workOrder.ProductId,
                LotId = finishedLot.Id,
                LocationId = qcLocationId,
                Qty = finalQty,
                QtyAfter = finishedBalance.QtyAvailable,
                ValuationRate = unitActualCost,
                TransactionDate = DateTime.UtcNow,
                UserId = userId,
                ReferenceNo = workOrder.Code
            });

            foreach (var reservation in reservations)
            {
                var balance = await _context.StockBalances.FirstOrDefaultAsync(sb =>
                    sb.ProductId == reservation.ProductId &&
                    sb.LotId == reservation.LotId &&
                    sb.LocationId == reservation.LocationId);

                if (balance is null ||
                    balance.QtyReserved < reservation.QtyReserved)
                {
                    throw new InvalidOperationException(
                        "Reserved material is no longer sufficient for backflush. Negative stock is not allowed.");
                }

                balance.QtyReserved -= reservation.QtyReserved;
                _context.StockTransactions.Add(new StockTransaction
                {
                    Type = TransactionType.Backflush,
                    ProductId = reservation.ProductId,
                    LotId = reservation.LotId,
                    LocationId = reservation.LocationId,
                    Qty = -reservation.QtyReserved,
                    QtyAfter = balance.QtyAvailable,
                    ValuationRate = reservation.Lot!.UnitPrice,
                    TransactionDate = DateTime.UtcNow,
                    UserId = userId,
                    ReferenceNo = workOrder.Code
                });
                _context.LotGenealogies.Add(new LotGenealogy
                {
                    OutputLotId = finishedLot.Id,
                    InputLotId = reservation.LotId,
                    QtyConsumed = reservation.QtyReserved
                });
            }

            workOrder.Status = WorkOrderStatus.Completed;
            await _context.SaveChangesAsync();
            await CommitIfSupportedAsync(transaction);
            await NotifyProgressAsync();
            return true;
        }
        catch
        {
            await RollbackIfSupportedAsync(transaction);
            throw;
        }
    }

    private async Task<IDbContextTransaction?> BeginTransactionIfSupportedAsync()
    {
        return _context.Database.IsRelational()
            ? await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable)
            : null;
    }

    private static async Task CommitIfSupportedAsync(IDbContextTransaction? transaction)
    {
        if (transaction != null)
        {
            await transaction.CommitAsync();
        }
    }

    private static async Task RollbackIfSupportedAsync(IDbContextTransaction? transaction)
    {
        if (transaction != null)
        {
            await transaction.RollbackAsync();
        }
    }

    private async Task NotifyProgressAsync()
    {
        if (_productionHub != null)
        {
            await _productionHub.Clients.All.SendAsync("ReceiveProgressUpdate");
        }
    }
}

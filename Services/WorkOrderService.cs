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
                var available = await _context.StockBalances
                    .Where(sb => sb.ProductId == requirement.ProductId)
                    .SumAsync(sb => sb.QtyAvailable);

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
                WorkOrderId = workOrder.Id
            };

            _context.Lots.Add(finishedLot);
            await _context.SaveChangesAsync();

            _context.StockBalances.Add(new StockBalance
            {
                ProductId = workOrder.ProductId,
                LotId = finishedLot.Id,
                LocationId = 1,
                QtyAvailable = 0m,
                QtyOnHold = finalQty
            });
            _context.StockTransactions.Add(new StockTransaction
            {
                Type = TransactionType.Receipt,
                ProductId = workOrder.ProductId,
                LotId = finishedLot.Id,
                LocationId = 1,
                Qty = finalQty,
                TransactionDate = DateTime.UtcNow,
                UserId = userId,
                ReferenceNo = workOrder.Code
            });

            var reservations = await _context.MaterialReservations
                .Where(r => r.WorkOrderId == workOrder.Id)
                .ToListAsync();
            foreach (var reservation in reservations)
            {
                var balance = await _context.StockBalances.FirstOrDefaultAsync(sb =>
                    sb.ProductId == reservation.ProductId &&
                    sb.LotId == reservation.LotId &&
                    sb.LocationId == reservation.LocationId);

                if (balance != null)
                {
                    balance.QtyReserved = Math.Max(0, balance.QtyReserved - reservation.QtyReserved);
                }

                _context.StockTransactions.Add(new StockTransaction
                {
                    Type = TransactionType.Backflush,
                    ProductId = reservation.ProductId,
                    LotId = reservation.LotId,
                    LocationId = reservation.LocationId,
                    Qty = -reservation.QtyReserved,
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
            ? await _context.Database.BeginTransactionAsync()
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

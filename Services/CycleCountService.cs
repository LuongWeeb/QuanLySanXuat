using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.DTOs;

namespace WmsMes.Web.Services;

public class CycleCountService : ICycleCountService
{
    private readonly ApplicationDbContext _context;
    private readonly IInventoryService _inventoryService;

    public CycleCountService(ApplicationDbContext context, IInventoryService inventoryService)
    {
        _context = context;
        _inventoryService = inventoryService;
    }

    public async Task<CycleCountOrder> CreateCycleCountOrderAsync(int warehouseId, string createdBy)
    {
        var balances = await _context.StockBalances
            .Where(balance => balance.Location!.Zone!.WarehouseId == warehouseId)
            .ToListAsync();

        var order = new CycleCountOrder
        {
            CountNumber = $"CC-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}",
            WarehouseId = warehouseId,
            CreatedBy = createdBy,
            Items = balances.Select(balance => new CycleCountItem
            {
                ProductId = balance.ProductId,
                LocationId = balance.LocationId,
                LotId = balance.LotId,
                SystemQty = balance.QtyAvailable
            }).ToList()
        };

        await _context.CycleCountOrders.AddAsync(order);
        await _context.SaveChangesAsync();
        return order;
    }

    public async Task<bool> RecordCountResultsAsync(int orderId, List<CountResultDto> results)
    {
        var order = await _context.CycleCountOrders
            .Include(cycleCountOrder => cycleCountOrder.Items)
            .FirstOrDefaultAsync(cycleCountOrder => cycleCountOrder.Id == orderId);

        if (order is null || order.Status is "Approved" or "Cancelled")
        {
            return false;
        }

        var resultByItemId = results.ToDictionary(result => result.CycleCountItemId);
        foreach (var item in order.Items)
        {
            if (resultByItemId.TryGetValue(item.Id, out var result))
            {
                item.CountedQty = result.CountedQty;
            }
        }

        order.Status = "InProgress";
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ApproveAndAdjustStockAsync(int orderId, string approvedBy)
    {
        await using var transaction = await BeginTransactionIfRelationalAsync();
        try
        {
            var order = await _context.CycleCountOrders
                .Include(cycleCountOrder => cycleCountOrder.Items)
                .FirstOrDefaultAsync(cycleCountOrder => cycleCountOrder.Id == orderId);

            if (order is null || order.Status is "Approved" or "Cancelled")
            {
                return false;
            }

            foreach (var item in order.Items.Where(item => item.CountedQty.HasValue && item.VarianceQty != 0))
            {
                var adjusted = await _inventoryService.AdjustStockAsync(
                    item.ProductId,
                    item.LotId,
                    item.LocationId,
                    item.VarianceQty,
                    approvedBy,
                    order.CountNumber);

                if (!adjusted)
                {
                    throw new InvalidOperationException($"Could not adjust stock for cycle count item {item.Id}.");
                }
            }

            order.Status = "Approved";
            order.ApprovedBy = approvedBy;
            order.CompletedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            await CommitIfRelationalAsync(transaction);
        }
        catch
        {
            await RollbackIfRelationalAsync(transaction);
            throw;
        }

        await _inventoryService.NotifyStockChangedAsync();
        return true;
    }

    private async Task<IDbContextTransaction?> BeginTransactionIfRelationalAsync()
    {
        return _context.Database.IsRelational() && _context.Database.CurrentTransaction is null
            ? await _context.Database.BeginTransactionAsync()
            : null;
    }

    private static async Task CommitIfRelationalAsync(IDbContextTransaction? transaction)
    {
        if (transaction is not null)
        {
            await transaction.CommitAsync();
        }
    }

    private static async Task RollbackIfRelationalAsync(IDbContextTransaction? transaction)
    {
        if (transaction is not null)
        {
            await transaction.RollbackAsync();
        }
    }
}

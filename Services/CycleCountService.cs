using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using System.Data;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.DTOs;

namespace WmsMes.Web.Services;

public class CycleCountService : ICycleCountService
{
    private readonly ApplicationDbContext _context;
    private readonly IInventoryService _inventoryService;
    private readonly ILogger<CycleCountService> _logger;

    public CycleCountService(
        ApplicationDbContext context,
        IInventoryService inventoryService,
        ILogger<CycleCountService>? logger = null)
    {
        _context = context;
        _inventoryService = inventoryService;
        _logger = logger ?? NullLogger<CycleCountService>.Instance;
    }

    public async Task<CycleCountOrder> CreateCycleCountOrderAsync(int warehouseId, string createdBy)
    {
        return await CreateOrderAsync(warehouseId, createdBy);
    }

    public Task<CycleCountOrder?> GetByIdAsync(int id)
    {
        return _context.CycleCountOrders
            .Include(order => order.Warehouse)
            .Include(order => order.Items).ThenInclude(item => item.Product)
            .Include(order => order.Items).ThenInclude(item => item.Location)
            .Include(order => order.Items).ThenInclude(item => item.Lot)
            .FirstOrDefaultAsync(order => order.Id == id);
    }

    public async Task<CycleCountOrder> CreateOrderAsync(
        int warehouseId,
        string createdBy)
    {
        await using var transaction = await BeginTransactionIfRelationalAsync();
        try
        {
        if (!await _context.Warehouses.AnyAsync(warehouse =>
                warehouse.Id == warehouseId && warehouse.IsActive))
        {
            throw new ArgumentException(
                "Warehouse does not exist or is inactive.",
                nameof(warehouseId));
        }

        var balances = await _context.StockBalances
            .Where(balance => balance.Location!.Zone!.WarehouseId == warehouseId &&
                              balance.Location.Code != QcService.QuarantineLocationCode &&
                              balance.QtyAvailable +
                                  balance.QtyReserved +
                                  balance.QtyOnHold > 0)
            .ToListAsync();
        var datePrefix = $"CC-{DateTime.UtcNow:yyyyMMdd}-";
        var dailyCount = await _context.CycleCountOrders.CountAsync(order =>
            order.CountNumber.StartsWith(datePrefix));
        if (dailyCount >= 999)
        {
            throw new InvalidOperationException(
                "The daily cycle count sequence has been exhausted.");
        }

        var order = new CycleCountOrder
        {
            CountNumber = $"{datePrefix}{dailyCount + 1:000}",
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
        await CommitIfRelationalAsync(transaction);
        return order;
        }
        catch
        {
            await RollbackIfRelationalAsync(transaction);
            throw;
        }
    }

    public Task<bool> UpdateCountedQtysAsync(
        int orderId,
        Dictionary<int, decimal> itemCounts)
    {
        var results = itemCounts
            .Select(entry => new CountResultDto
            {
                CycleCountItemId = entry.Key,
                CountedQty = entry.Value
            })
            .ToList();
        return RecordCountResultsAsync(orderId, results);
    }

    public Task<bool> ApproveAndAdjustLedgerAsync(
        int orderId,
        string managerUserId)
    {
        return ApproveAndAdjustStockAsync(orderId, managerUserId);
    }

    public async Task<bool> AddDiscoveredItemAsync(
        int orderId,
        string locationCode,
        string lotNo,
        decimal countedQty)
    {
        if (countedQty < 0 ||
            string.IsNullOrWhiteSpace(locationCode) ||
            string.IsNullOrWhiteSpace(lotNo))
        {
            return false;
        }

        var order = await _context.CycleCountOrders
            .Include(item => item.Items)
            .SingleOrDefaultAsync(item => item.Id == orderId);
        if (order is null || order.Status is "Completed" or "Approved" or "Cancelled")
            return false;

        var normalizedLocation = locationCode.Trim();
        var normalizedLot = lotNo.Trim();
        var location = await _context.Locations
            .Include(item => item.Zone)
            .SingleOrDefaultAsync(item =>
                item.Code == normalizedLocation &&
                item.Zone!.WarehouseId == order.WarehouseId);
        var lot = await _context.Lots
            .SingleOrDefaultAsync(item => item.LotNo == normalizedLot);
        if (location is null ||
            lot is null ||
            order.Items.Any(item =>
                item.LocationId == location.Id &&
                item.LotId == lot.Id))
        {
            return false;
        }

        order.Items.Add(new CycleCountItem
        {
            ProductId = lot.ProductId,
            LocationId = location.Id,
            LotId = lot.Id,
            SystemQty = 0,
            CountedQty = countedQty
        });
        order.Status = "InProgress";
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> RecordCountResultsAsync(int orderId, List<CountResultDto> results)
    {
        if (_context.Database.IsRelational())
        {
            await using var transaction = await BeginTransactionIfRelationalAsync();
            try
            {
                var orderSnapshot = await _context.CycleCountOrders
                    .AsNoTracking()
                    .Include(order => order.Items)
                    .SingleOrDefaultAsync(order => order.Id == orderId);
                if (orderSnapshot is null ||
                    orderSnapshot.Status is "Approved" or "Cancelled")
                    return false;

                var itemIds = orderSnapshot.Items.Select(item => item.Id).ToHashSet();
                if (results.Any(result =>
                        result.CountedQty < 0 ||
                        !itemIds.Contains(result.CycleCountItemId)) ||
                    results.Select(result => result.CycleCountItemId).Distinct().Count() != results.Count)
                    return false;

                foreach (var result in results)
                {
                    var countedQty = result.CountedQty;
                    var affected = await _context.CycleCountItems
                        .Where(item =>
                            item.Id == result.CycleCountItemId &&
                            item.CycleCountOrderId == orderId &&
                            item.CycleCountOrder!.Status != "Approved" &&
                            item.CycleCountOrder.Status != "Cancelled")
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(item => item.CountedQty, countedQty));
                    if (affected != 1)
                        return false;
                }

                var allCounted = !await _context.CycleCountItems.AnyAsync(item =>
                    item.CycleCountOrderId == orderId &&
                    !item.CountedQty.HasValue);
                var nextStatus = allCounted ? "Completed" : "InProgress";
                var completedAt = allCounted ? DateTime.UtcNow : (DateTime?)null;
                var claimed = await _context.CycleCountOrders
                    .Where(order =>
                        order.Id == orderId &&
                        order.Status != "Approved" &&
                        order.Status != "Cancelled")
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(order => order.Status, nextStatus)
                        .SetProperty(order => order.CompletedAt, completedAt));
                if (claimed != 1)
                    return false;

                await CommitIfRelationalAsync(transaction);
                return true;
            }
            catch
            {
                await RollbackIfRelationalAsync(transaction);
                throw;
            }
        }

        var order = await _context.CycleCountOrders
            .Include(cycleCountOrder => cycleCountOrder.Items)
            .FirstOrDefaultAsync(cycleCountOrder => cycleCountOrder.Id == orderId);

        if (order is null || order.Status is "Approved" or "Cancelled")
        {
            return false;
        }

        var itemsById = order.Items.ToDictionary(item => item.Id);
        if (results.Any(result => result.CountedQty < 0 || !itemsById.ContainsKey(result.CycleCountItemId)) ||
            results.Select(result => result.CycleCountItemId).Distinct().Count() != results.Count)
        {
            return false;
        }

        foreach (var result in results)
        {
            itemsById[result.CycleCountItemId].CountedQty = result.CountedQty;
        }

        var isCompleted = order.Items.All(item => item.CountedQty.HasValue);
        order.Status = isCompleted ? "Completed" : "InProgress";
        order.CompletedAt = isCompleted ? DateTime.UtcNow : null;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ApproveAndAdjustStockAsync(int orderId, string approvedBy)
    {
        var hasAmbientTransaction = _context.Database.CurrentTransaction is not null;
        await using var transaction = await BeginTransactionIfRelationalAsync();
        try
        {
            CycleCountOrder order;
            if (_context.Database.IsRelational())
            {
                var completedAt = DateTime.UtcNow;
                var claimed = await _context.CycleCountOrders
                    .Where(cycleCountOrder => cycleCountOrder.Id == orderId && cycleCountOrder.Status == "Completed")
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(cycleCountOrder => cycleCountOrder.Status, "Approved")
                        .SetProperty(cycleCountOrder => cycleCountOrder.ApprovedBy, approvedBy)
                        .SetProperty(
                            cycleCountOrder => cycleCountOrder.CompletedAt,
                            cycleCountOrder => cycleCountOrder.CompletedAt ?? completedAt));

                if (claimed != 1)
                {
                    return false;
                }

                order = await _context.CycleCountOrders
                    .AsNoTracking()
                    .Include(cycleCountOrder => cycleCountOrder.Items)
                    .SingleAsync(cycleCountOrder => cycleCountOrder.Id == orderId);
            }
            else
            {
                var trackedOrder = await _context.CycleCountOrders
                    .Include(cycleCountOrder => cycleCountOrder.Items)
                    .FirstOrDefaultAsync(cycleCountOrder => cycleCountOrder.Id == orderId);

                if (trackedOrder is null || trackedOrder.Status != "Completed")
                {
                    return false;
                }

                order = trackedOrder;
            }

            foreach (var item in order.Items.Where(item => item.CountedQty.HasValue && item.VarianceQty != 0))
            {
                var balanceExists = await _context.StockBalances.AnyAsync(balance =>
                    balance.ProductId == item.ProductId &&
                    balance.LotId == item.LotId &&
                    balance.LocationId == item.LocationId);
                if (!balanceExists)
                {
                    if (item.VarianceQty < 0)
                        throw new InvalidOperationException($"Cannot create negative stock for cycle count item {item.Id}.");

                    _context.StockBalances.Add(new StockBalance
                    {
                        ProductId = item.ProductId,
                        LotId = item.LotId,
                        LocationId = item.LocationId,
                        QtyAvailable = item.VarianceQty
                    });
                    _context.StockTransactions.Add(new StockTransaction
                    {
                        Type = Domain.Enums.TransactionType.Adjust,
                        ProductId = item.ProductId,
                        LotId = item.LotId,
                        LocationId = item.LocationId,
                        Qty = item.VarianceQty,
                        QtyAfter = item.VarianceQty,
                        ValuationRate = item.Lot?.UnitPrice ?? 0,
                        TransactionDate = DateTime.UtcNow,
                        UserId = approvedBy,
                        ReferenceNo = order.CountNumber
                    });
                    continue;
                }

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

            await _context.SaveChangesAsync();

            if (!_context.Database.IsRelational())
            {
                order.Status = "Approved";
                order.ApprovedBy = approvedBy;
                order.CompletedAt ??= DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }

            await CommitIfRelationalAsync(transaction);
        }
        catch
        {
            await RollbackIfRelationalAsync(transaction);
            throw;
        }

        if (!hasAmbientTransaction)
        {
            await NotifyStockChangedSafelyAsync();
        }

        return true;
    }

    private async Task<IDbContextTransaction?> BeginTransactionIfRelationalAsync()
    {
        return _context.Database.IsRelational() && _context.Database.CurrentTransaction is null
            ? await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable)
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

    private async Task NotifyStockChangedSafelyAsync()
    {
        try
        {
            await _inventoryService.NotifyStockChangedAsync();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Cycle count approval committed but realtime notification failed.");
        }
    }
}

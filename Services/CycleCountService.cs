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

    public async Task<CycleCountOrder?> GetByIdAsync(int id)
    {
        var order = await _context.CycleCountOrders
            .Include(order => order.Warehouse)
            .Include(order => order.Items).ThenInclude(item => item.Product)
            .Include(order => order.Items).ThenInclude(item => item.Location)
            .Include(order => order.Items).ThenInclude(item => item.Lot)
            .FirstOrDefaultAsync(order => order.Id == id);
        if (order is not null)
        {
            await CycleCountReconciliation.PopulateExpectedAtCountQuantitiesAsync(
                _context,
                order);
        }

        return order;
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
        return UpdateCountedQtysAsync(orderId, itemCounts, []);
    }

    public Task<bool> UpdateCountedQtysAsync(
        int orderId,
        Dictionary<int, decimal> itemCounts,
        Dictionary<int, string?> itemReasons)
    {
        return SaveCountResultsAsync(orderId, itemCounts, itemReasons);
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

        var normalizedLocation = locationCode.Trim();
        var normalizedLot = lotNo.Trim();
        await using var transaction = await BeginTransactionIfRelationalAsync();
        try
        {
            var warehouseId = await _context.CycleCountOrders
                .AsNoTracking()
                .Where(order =>
                    order.Id == orderId &&
                    (order.Status == "Draft" || order.Status == "InProgress"))
                .Select(order => (int?)order.WarehouseId)
                .SingleOrDefaultAsync();
            if (!warehouseId.HasValue)
            {
                return false;
            }

            var location = await _context.Locations
                .AsNoTracking()
                .SingleOrDefaultAsync(item =>
                    item.Code == normalizedLocation &&
                    item.Zone!.WarehouseId == warehouseId.Value);
            var lot = await _context.Lots
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.LotNo == normalizedLot);
            if (location is null || lot is null)
            {
                return false;
            }

            if (_context.Database.IsRelational())
            {
                var claimed = await _context.CycleCountOrders
                    .Where(order =>
                        order.Id == orderId &&
                        (order.Status == "Draft" || order.Status == "InProgress"))
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(order => order.Status, "InProgress"));
                if (claimed != 1)
                {
                    return false;
                }
            }
            else
            {
                var trackedOrder = await _context.CycleCountOrders
                    .SingleOrDefaultAsync(order => order.Id == orderId);
                if (trackedOrder is null ||
                    trackedOrder.Status is not ("Draft" or "InProgress"))
                {
                    return false;
                }

                trackedOrder.Status = "InProgress";
            }

            var duplicate = await _context.CycleCountItems
                .AsNoTracking()
                .AnyAsync(item =>
                    item.CycleCountOrderId == orderId &&
                    item.LocationId == location.Id &&
                    item.LotId == lot.Id);
            if (duplicate)
            {
                return false;
            }

            _context.CycleCountItems.Add(new CycleCountItem
            {
                CycleCountOrderId = orderId,
                ProductId = lot.ProductId,
                LocationId = location.Id,
                LotId = lot.Id,
                SystemQty = 0,
                CountedQty = countedQty
            });
            await _context.SaveChangesAsync();
            await CommitIfRelationalAsync(transaction);
            return true;
        }
        catch (DbUpdateException)
        {
            await RollbackIfRelationalAsync(transaction);
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }

            _context.ChangeTracker.Clear();
            if (await _context.CycleCountItems.AsNoTracking().AnyAsync(item =>
                    item.CycleCountOrderId == orderId &&
                    item.Location!.Code == normalizedLocation &&
                    item.Lot!.LotNo == normalizedLot))
            {
                return false;
            }

            throw;
        }
        catch
        {
            await RollbackIfRelationalAsync(transaction);
            throw;
        }
    }

    public async Task<bool> RecordCountResultsAsync(int orderId, List<CountResultDto> results)
    {
        if (results.Select(result => result.CycleCountItemId).Distinct().Count() !=
            results.Count)
        {
            return false;
        }

        return await SaveCountResultsAsync(
            orderId,
            results.ToDictionary(
                result => result.CycleCountItemId,
                result => result.CountedQty),
            new Dictionary<int, string?>());
    }

    private async Task<bool> SaveCountResultsAsync(
        int orderId,
        IReadOnlyDictionary<int, decimal> itemCounts,
        IReadOnlyDictionary<int, string?> itemReasons)
    {
        var normalizedReasons = new Dictionary<int, string?>();
        foreach (var (itemId, reason) in itemReasons)
        {
            var normalizedReason = NormalizeReason(reason);
            if (normalizedReason?.Length > 250)
            {
                return false;
            }

            normalizedReasons[itemId] = normalizedReason;
        }

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
                    orderSnapshot.Status is not ("Draft" or "InProgress"))
                    return false;

                var itemIds = orderSnapshot.Items.Select(item => item.Id).ToHashSet();
                if (itemCounts.Any(entry =>
                        entry.Value < 0 ||
                        !itemIds.Contains(entry.Key)) ||
                    normalizedReasons.Keys.Any(itemId => !itemIds.Contains(itemId)))
                    return false;

                foreach (var itemId in itemCounts.Keys
                    .Concat(normalizedReasons.Keys)
                    .Distinct())
                {
                    var query = _context.CycleCountItems
                        .Where(item =>
                            item.Id == itemId &&
                            item.CycleCountOrderId == orderId &&
                            (item.CycleCountOrder!.Status == "Draft" ||
                             item.CycleCountOrder.Status == "InProgress"));
                    var hasCount = itemCounts.TryGetValue(itemId, out var countedQty);
                    var hasReason = normalizedReasons.TryGetValue(
                        itemId,
                        out var normalizedReason);
                    int affected;
                    if (hasCount && hasReason)
                    {
                        affected = await query.ExecuteUpdateAsync(setters => setters
                            .SetProperty(item => item.CountedQty, countedQty)
                            .SetProperty(item => item.ReasonNote, normalizedReason));
                    }
                    else if (hasCount)
                    {
                        affected = await query.ExecuteUpdateAsync(setters => setters
                            .SetProperty(item => item.CountedQty, countedQty));
                    }
                    else
                    {
                        affected = await query.ExecuteUpdateAsync(setters => setters
                            .SetProperty(item => item.ReasonNote, normalizedReason));
                    }

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
                        (order.Status == "Draft" ||
                         order.Status == "InProgress"))
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

        if (order is null ||
            order.Status is not ("Draft" or "InProgress"))
        {
            return false;
        }

        var itemsById = order.Items.ToDictionary(item => item.Id);
        if (itemCounts.Any(entry =>
                entry.Value < 0 ||
                !itemsById.ContainsKey(entry.Key)) ||
            normalizedReasons.Keys.Any(itemId => !itemsById.ContainsKey(itemId)))
        {
            return false;
        }

        foreach (var (itemId, countedQty) in itemCounts)
        {
            itemsById[itemId].CountedQty = countedQty;
        }

        foreach (var (itemId, normalizedReason) in normalizedReasons)
        {
            itemsById[itemId].ReasonNote = normalizedReason;
        }

        var isCompleted = order.Items.All(item => item.CountedQty.HasValue);
        order.Status = isCompleted ? "Completed" : "InProgress";
        order.CompletedAt = isCompleted ? DateTime.UtcNow : null;
        await _context.SaveChangesAsync();
        return true;
    }

    private static string? NormalizeReason(string? reason)
    {
        var normalized = reason?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
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
                        .ThenInclude(item => item.Lot)
                    .SingleAsync(cycleCountOrder => cycleCountOrder.Id == orderId);
            }
            else
            {
                var trackedOrder = await _context.CycleCountOrders
                    .Include(cycleCountOrder => cycleCountOrder.Items)
                        .ThenInclude(item => item.Lot)
                    .FirstOrDefaultAsync(cycleCountOrder => cycleCountOrder.Id == orderId);

                if (trackedOrder is null || trackedOrder.Status != "Completed")
                {
                    return false;
                }

                order = trackedOrder;
            }

            await CycleCountReconciliation.PopulateExpectedAtCountQuantitiesAsync(
                _context,
                order);

            foreach (var item in order.Items.Where(item =>
                         item.CountedQty.HasValue &&
                         item.AuthoritativeVarianceQty != 0))
            {
                var adjustmentQty = item.AuthoritativeVarianceQty;
                var balanceExists = await _context.StockBalances.AnyAsync(balance =>
                    balance.ProductId == item.ProductId &&
                    balance.LotId == item.LotId &&
                    balance.LocationId == item.LocationId);
                if (!balanceExists)
                {
                    if (adjustmentQty < 0)
                        throw new InvalidOperationException($"Cannot create negative stock for cycle count item {item.Id}.");

                    _context.StockBalances.Add(new StockBalance
                    {
                        ProductId = item.ProductId,
                        LotId = item.LotId,
                        LocationId = item.LocationId,
                        QtyAvailable = adjustmentQty
                    });
                    _context.StockTransactions.Add(new StockTransaction
                    {
                        Type = Domain.Enums.TransactionType.Adjust,
                        ProductId = item.ProductId,
                        LotId = item.LotId,
                        LocationId = item.LocationId,
                        Qty = adjustmentQty,
                        QtyAfter = adjustmentQty,
                        ValuationRate = item.Lot?.UnitPrice
                            ?? throw new InvalidOperationException(
                                $"The cycle count lot for item {item.Id} no longer exists."),
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
                    adjustmentQty,
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

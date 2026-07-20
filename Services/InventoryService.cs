using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Domain.Enums;
using WmsMes.Web.DTOs;
using WmsMes.Web.Hubs;

namespace WmsMes.Web.Services;

public class InventoryService : IInventoryService
{
    private readonly ApplicationDbContext _context;
    private readonly IHubContext<InventoryHub>? _hubContext;
    private readonly ILogger<InventoryService> _logger;

    public InventoryService(ApplicationDbContext context)
        : this(context, null, null)
    {
    }

    public InventoryService(ApplicationDbContext context, IHubContext<InventoryHub>? hubContext)
        : this(context, hubContext, null)
    {
    }

    public InventoryService(ApplicationDbContext context, IHubContext<InventoryHub>? hubContext, ILogger<InventoryService>? logger)
    {
        _context = context;
        _hubContext = hubContext;
        _logger = logger ?? NullLogger<InventoryService>.Instance;
    }

    public async Task<List<PickingRecommendationDto>> GetPickingRecommendationsAsync(int productId, decimal requiredQty, PickingStrategy strategy)
    {
        if (requiredQty <= 0)
        {
            return [];
        }

        var query = _context.StockBalances
            .AsNoTracking()
            .Include(balance => balance.Product)
            .Include(balance => balance.Lot)
            .Include(balance => balance.Location)
            .Where(balance => balance.ProductId == productId &&
                              balance.QtyAvailable > 0 &&
                              balance.Location!.Code != QcService.QuarantineLocationCode);

        var balances = strategy == PickingStrategy.FIFO
            ? await query.OrderBy(balance => balance.Lot!.ManufactureDate).ThenBy(balance => balance.Id).ToListAsync()
            : await query.OrderBy(balance => balance.Lot!.ExpiryDate ?? DateTime.MaxValue)
                .ThenBy(balance => balance.Lot!.ManufactureDate)
                .ToListAsync();

        var remainingQty = requiredQty;
        var recommendations = new List<PickingRecommendationDto>();

        foreach (var balance in balances)
        {
            if (remainingQty <= 0)
            {
                break;
            }

            var recommendedQty = Math.Min(balance.QtyAvailable, remainingQty);
            recommendations.Add(new PickingRecommendationDto
            {
                ProductId = balance.ProductId,
                ProductCode = balance.Product?.Code ?? string.Empty,
                ProductName = balance.Product?.Name ?? string.Empty,
                LocationId = balance.LocationId,
                LocationCode = balance.Location?.Code ?? string.Empty,
                LotId = balance.LotId,
                LotNo = balance.Lot?.LotNo ?? string.Empty,
                ExpiryDate = balance.Lot?.ExpiryDate,
                ManufactureDate = balance.Lot?.ManufactureDate ?? DateTime.MinValue,
                AvailableQty = balance.QtyAvailable,
                RecommendedQty = recommendedQty
            });
            remainingQty -= recommendedQty;
        }

        return recommendations;
    }

    public async Task<IEnumerable<StockBalance>> GetSuggestedLotsAsync(int productId, decimal qty)
    {
        if (qty <= 0)
        {
            return Enumerable.Empty<StockBalance>();
        }

        var product = await _context.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == productId);

        if (product is null)
        {
            return Enumerable.Empty<StockBalance>();
        }

        var query = _context.StockBalances
            .AsNoTracking()
            .Include(balance => balance.Lot)
            .Include(balance => balance.Location)
            .Where(balance => balance.ProductId == productId && balance.QtyAvailable > 0);

        query = product.ShelfLifeDays.HasValue
            ? query.OrderBy(balance => balance.Lot!.ExpiryDate ?? DateTime.MaxValue).ThenBy(balance => balance.LotId)
            : query.OrderBy(balance => balance.LotId);

        var balances = await query.ToListAsync();
        var remainingQty = qty;
        var suggestions = new List<StockBalance>();

        foreach (var balance in balances)
        {
            if (remainingQty <= 0)
            {
                break;
            }

            var suggestedQty = Math.Min(balance.QtyAvailable, remainingQty);
            suggestions.Add(new StockBalance
            {
                Id = balance.Id,
                ProductId = balance.ProductId,
                Product = balance.Product,
                LotId = balance.LotId,
                Lot = balance.Lot,
                LocationId = balance.LocationId,
                Location = balance.Location,
                QtyAvailable = suggestedQty,
                QtyReserved = balance.QtyReserved,
                QtyOnHold = balance.QtyOnHold
            });

            remainingQty -= suggestedQty;
        }

        return suggestions;
    }

    public Task<bool> CompleteGoodsReceiptAsync(int receiptId, string userId) =>
        CompleteGoodsReceiptCoreAsync(receiptId, userId, notify: true);

    public Task<bool> CompleteGoodsReceiptWithoutNotificationAsync(int receiptId, string userId) =>
        CompleteGoodsReceiptCoreAsync(receiptId, userId, notify: false);

    private async Task<bool> CompleteGoodsReceiptCoreAsync(int receiptId, string userId, bool notify)
    {
        var hasAmbientTransaction = _context.Database.CurrentTransaction is not null;
        await using var transaction = await BeginTransactionIfRelationalAsync();
        try
        {
            var receipt = await _context.GoodsReceipts
                .Include(r => r.Lines)
                .FirstOrDefaultAsync(r => r.Id == receiptId);

            if (receipt is null || receipt.Status == DocumentStatus.Completed)
            {
                return false;
            }

            foreach (var line in receipt.Lines)
            {
                var lot = await _context.Lots
                    .FirstOrDefaultAsync(l => l.LotNo == line.LotNo && l.ProductId == line.ProductId);

                if (lot is null)
                {
                    lot = new Lot
                    {
                        LotNo = line.LotNo,
                        ProductId = line.ProductId,
                        ManufactureDate = line.ManufactureDate,
                        ExpiryDate = line.ExpiryDate,
                        Qty = line.Qty,
                        UnitPrice = line.UnitPrice
                    };
                    await _context.Lots.AddAsync(lot);
                    await _context.SaveChangesAsync();
                }
                else
                {
                    var totalQty = lot.Qty + line.Qty;
                    lot.UnitPrice = totalQty > 0
                        ? Math.Round(((lot.Qty * lot.UnitPrice) + (line.Qty * line.UnitPrice)) / totalQty, 2, MidpointRounding.AwayFromZero)
                        : line.UnitPrice;
                    lot.Qty += line.Qty;
                }

                var balance = await _context.StockBalances
                    .FirstOrDefaultAsync(sb =>
                        sb.ProductId == line.ProductId &&
                        sb.LotId == lot.Id &&
                        sb.LocationId == line.LocationId);

                if (balance is null)
                {
                    balance = new StockBalance
                    {
                        ProductId = line.ProductId,
                        LotId = lot.Id,
                        LocationId = line.LocationId,
                        QtyAvailable = 0,
                        QtyReserved = 0,
                        QtyOnHold = 0
                    };
                    await _context.StockBalances.AddAsync(balance);
                }

                balance.QtyAvailable += line.Qty;

                await _context.StockTransactions.AddAsync(new StockTransaction
                {
                    Type = TransactionType.Receipt,
                    ProductId = line.ProductId,
                    LotId = lot.Id,
                    LocationId = line.LocationId,
                    Qty = line.Qty,
                    TransactionDate = DateTime.UtcNow,
                    UserId = userId,
                    ReferenceNo = receipt.ReceiptNo
                });
            }

            receipt.Status = DocumentStatus.Completed;
            await _context.SaveChangesAsync();
            await CommitIfRelationalAsync(transaction);
        }
        catch
        {
            await RollbackIfRelationalAsync(transaction);
            throw;
        }

        if (notify && !hasAmbientTransaction) await NotifyStockChangedSafelyAsync();
        return true;
    }

    public Task<bool> CompleteGoodsIssueAsync(int issueId, string userId) =>
        CompleteGoodsIssueCoreAsync(issueId, userId, notify: true);

    public Task<bool> CompleteGoodsIssueWithoutNotificationAsync(int issueId, string userId) =>
        CompleteGoodsIssueCoreAsync(issueId, userId, notify: false);

    private async Task<bool> CompleteGoodsIssueCoreAsync(int issueId, string userId, bool notify)
    {
        var hasAmbientTransaction = _context.Database.CurrentTransaction is not null;
        await using var transaction = await BeginTransactionIfRelationalAsync();
        try
        {
            var issue = await _context.GoodsIssues
                .Include(i => i.Lines)
                .FirstOrDefaultAsync(i => i.Id == issueId);

            if (issue is null || issue.Status == DocumentStatus.Completed)
            {
                return false;
            }

            foreach (var line in issue.Lines)
            {
                if (_context.Database.IsRelational())
                {
                    var updated = await _context.StockBalances
                        .Where(sb =>
                            sb.ProductId == line.ProductId &&
                            sb.LotId == line.LotId &&
                            sb.LocationId == line.LocationId &&
                            sb.QtyAvailable >= line.Qty)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(sb => sb.QtyAvailable, sb => sb.QtyAvailable - line.Qty));
                    if (updated == 0)
                    {
                        throw new InvalidOperationException("Not enough available stock. Negative stock is not allowed.");
                    }
                }
                else
                {
                    var balance = await _context.StockBalances
                        .FirstOrDefaultAsync(sb =>
                            sb.ProductId == line.ProductId &&
                            sb.LotId == line.LotId &&
                            sb.LocationId == line.LocationId);

                    if (balance is null || balance.QtyAvailable < line.Qty)
                    {
                        throw new InvalidOperationException("Not enough available stock. Negative stock is not allowed.");
                    }

                    balance.QtyAvailable -= line.Qty;
                }

                await _context.StockTransactions.AddAsync(new StockTransaction
                {
                    Type = TransactionType.Issue,
                    ProductId = line.ProductId,
                    LotId = line.LotId,
                    LocationId = line.LocationId,
                    Qty = -line.Qty,
                    TransactionDate = DateTime.UtcNow,
                    UserId = userId,
                    ReferenceNo = issue.IssueNo
                });
            }

            issue.Status = DocumentStatus.Completed;
            await _context.SaveChangesAsync();
            await CommitIfRelationalAsync(transaction);
        }
        catch
        {
            await RollbackIfRelationalAsync(transaction);
            throw;
        }


        if (notify && !hasAmbientTransaction) await NotifyStockChangedSafelyAsync();
        return true;
    }

    public async Task<bool> StartStocktakeAsync(int stocktakeId)
    {
        await using var transaction = await BeginTransactionIfRelationalAsync();
        try
        {
            var stocktake = await _context.Stocktakes
                .FirstOrDefaultAsync(s => s.Id == stocktakeId);

            if (stocktake is null || stocktake.Status != StocktakeStatus.Draft)
            {
                return false;
            }

            var balances = await _context.StockBalances
                .Where(sb => sb.LocationId == stocktake.LocationId)
                .ToListAsync();

            foreach (var balance in balances)
            {
                var qtySystem = balance.QtyAvailable;
                balance.QtyOnHold += balance.QtyAvailable;
                balance.QtyAvailable = 0;

                await _context.StocktakeLines.AddAsync(new StocktakeLine
                {
                    StocktakeId = stocktakeId,
                    ProductId = balance.ProductId,
                    LotId = balance.LotId,
                    QtySystem = qtySystem,
                    QtyCounted = 0,
                    QtyDiscrepancy = 0
                });
            }

            stocktake.Status = StocktakeStatus.Counting;
            await _context.SaveChangesAsync();
            await CommitIfRelationalAsync(transaction);
            return true;
        }
        catch
        {
            await RollbackIfRelationalAsync(transaction);
            throw;
        }
    }

    public async Task<bool> ApproveStocktakeAsync(int stocktakeId, string userId)
    {
        await using var transaction = await BeginTransactionIfRelationalAsync();
        try
        {
            var stocktake = await _context.Stocktakes
                .Include(s => s.Lines)
                .FirstOrDefaultAsync(s => s.Id == stocktakeId);

            if (stocktake is null || stocktake.Status != StocktakeStatus.AwaitingApproval)
            {
                return false;
            }

            foreach (var line in stocktake.Lines)
            {
                var balance = await _context.StockBalances
                    .FirstOrDefaultAsync(sb =>
                        sb.ProductId == line.ProductId &&
                        sb.LotId == line.LotId &&
                        sb.LocationId == stocktake.LocationId);

                if (balance is null)
                {
                    continue;
                }

                var discrepancy = line.QtyCounted - line.QtySystem;
                line.QtyDiscrepancy = discrepancy;
                balance.QtyAvailable = line.QtyCounted;
                balance.QtyOnHold = Math.Max(0, balance.QtyOnHold - line.QtySystem);

                if (discrepancy != 0)
                {
                    await _context.StockTransactions.AddAsync(new StockTransaction
                    {
                        Type = TransactionType.Adjust,
                        ProductId = line.ProductId,
                        LotId = line.LotId,
                        LocationId = stocktake.LocationId,
                        Qty = discrepancy,
                        TransactionDate = DateTime.UtcNow,
                        UserId = userId,
                        ReferenceNo = stocktake.StocktakeNo
                    });
                }
            }

            stocktake.Status = StocktakeStatus.Completed;
            await _context.SaveChangesAsync();
            await CommitIfRelationalAsync(transaction);
            await NotifyStockChangedAsync();
            return true;
        }
        catch
        {
            await RollbackIfRelationalAsync(transaction);
            throw;
        }
    }

    public async Task<bool> AdjustStockAsync(int productId, int lotId, int locationId, decimal adjustmentQty, string userId, string referenceNo)
    {
        if (adjustmentQty == 0)
        {
            return true;
        }

        var hasAmbientTransaction = _context.Database.CurrentTransaction is not null;
        await using var transaction = await BeginTransactionIfRelationalAsync();
        try
        {
            if (_context.Database.IsRelational())
            {
                var updated = await _context.StockBalances
                    .Where(stockBalance =>
                        stockBalance.ProductId == productId &&
                        stockBalance.LotId == lotId &&
                        stockBalance.LocationId == locationId &&
                        stockBalance.QtyAvailable + adjustmentQty >= 0)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(stockBalance => stockBalance.QtyAvailable,
                            stockBalance => stockBalance.QtyAvailable + adjustmentQty));

                if (updated != 1)
                {
                    return false;
                }

                var trackedBalance = _context.ChangeTracker.Entries<StockBalance>()
                    .FirstOrDefault(entry =>
                        entry.Entity.ProductId == productId &&
                        entry.Entity.LotId == lotId &&
                        entry.Entity.LocationId == locationId);
                if (trackedBalance is not null)
                {
                    trackedBalance.State = EntityState.Detached;
                }
            }
            else
            {
                var balance = await _context.StockBalances
                    .FirstOrDefaultAsync(stockBalance =>
                        stockBalance.ProductId == productId &&
                        stockBalance.LotId == lotId &&
                        stockBalance.LocationId == locationId);

                if (balance is null || balance.QtyAvailable + adjustmentQty < 0)
                {
                    return false;
                }

                balance.QtyAvailable += adjustmentQty;
            }

            await _context.StockTransactions.AddAsync(new StockTransaction
            {
                Type = TransactionType.Adjust,
                ProductId = productId,
                LotId = lotId,
                LocationId = locationId,
                Qty = adjustmentQty,
                TransactionDate = DateTime.UtcNow,
                UserId = userId,
                ReferenceNo = referenceNo
            });

            await _context.SaveChangesAsync();
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

    public async Task NotifyStockChangedAsync()
    {
        if (_hubContext is not null)
        {
            await _hubContext.Clients.All.SendAsync("ReceiveStockUpdate");
        }
    }

    private async Task NotifyStockChangedSafelyAsync()
    {
        try
        {
            await NotifyStockChangedAsync();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Inventory operation committed but realtime notification failed.");
        }
    }
}

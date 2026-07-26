using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using System.Data;
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

            foreach (var line in receipt.Lines
                .OrderBy(line => line.ProductId)
                .ThenBy(line => line.LotNo.Trim(), StringComparer.OrdinalIgnoreCase)
                .ThenBy(line => line.LocationId)
                .ThenBy(line => line.Id))
            {
                if (line.Qty <= 0)
                {
                    throw new InvalidOperationException("Quantity must be greater than zero.");
                }

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
                    QtyAfter = balance.QtyAvailable,
                    ValuationRate = lot.UnitPrice,
                    IsCancelled = false,
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

            foreach (var line in issue.Lines
                .OrderBy(line => line.ProductId)
                .ThenBy(line => line.LotId)
                .ThenBy(line => line.LocationId)
                .ThenBy(line => line.Id))
            {
                if (line.Qty <= 0)
                {
                    throw new InvalidOperationException("Quantity must be greater than zero.");
                }

                decimal qtyAfter;
                if (_context.Database.IsRelational())
                {
                    var updated = await _context.StockBalances
                        .Where(sb =>
                            sb.ProductId == line.ProductId &&
                            sb.LotId == line.LotId &&
                            sb.LocationId == line.LocationId &&
                            sb.Location!.Code != QcService.QuarantineLocationCode &&
                            sb.QtyAvailable >= line.Qty)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(sb => sb.QtyAvailable, sb => sb.QtyAvailable - line.Qty));
                    if (updated == 0)
                    {
                        throw new InvalidOperationException("Not enough available stock. Negative stock is not allowed.");
                    }

                    qtyAfter = await _context.StockBalances
                        .Where(sb =>
                            sb.ProductId == line.ProductId &&
                            sb.LotId == line.LotId &&
                            sb.LocationId == line.LocationId)
                        .Select(sb => sb.QtyAvailable)
                        .SingleAsync();
                }
                else
                {
                    var balance = await _context.StockBalances
                        .FirstOrDefaultAsync(sb =>
                            sb.ProductId == line.ProductId &&
                            sb.LotId == line.LotId &&
                            sb.LocationId == line.LocationId &&
                            sb.Location!.Code != QcService.QuarantineLocationCode);

                    if (balance is null || balance.QtyAvailable < line.Qty)
                    {
                        throw new InvalidOperationException("Not enough available stock. Negative stock is not allowed.");
                    }

                    balance.QtyAvailable -= line.Qty;
                    qtyAfter = balance.QtyAvailable;
                }

                var lot = await _context.Lots.FindAsync(line.LotId);
                await _context.StockTransactions.AddAsync(new StockTransaction
                {
                    Type = TransactionType.Issue,
                    ProductId = line.ProductId,
                    LotId = line.LotId,
                    LocationId = line.LocationId,
                    Qty = -line.Qty,
                    QtyAfter = qtyAfter,
                    ValuationRate = lot?.UnitPrice ?? 0m,
                    IsCancelled = false,
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

    public async Task<bool> CancelGoodsReceiptAsync(int receiptId, string userId)
    {
        var changeTrackerSnapshot = CaptureChangeTracker();
        var transactionScope = await BeginCancellationTransactionAsync("CancelGoodsReceipt");
        await using var ownedTransaction = transactionScope.OwnedTransaction;
        try
        {
            var receipt = await _context.GoodsReceipts
                .Include(receipt => receipt.Lines)
                .FirstOrDefaultAsync(receipt => receipt.Id == receiptId);

            if (receipt is null || receipt.Status != DocumentStatus.Completed)
            {
                await CompleteCancellationTransactionAsync(transactionScope);
                return false;
            }

            if (receipt.Lines.Any(line => line.Qty <= 0))
            {
                throw new InvalidOperationException("Quantity must be greater than zero.");
            }

            var resolvedLines = await ResolveReceiptCancellationLinesAsync(receipt.Lines);
            EnsureReceiptCancellationTargetsAreClean(resolvedLines);

            if (_context.Database.IsRelational())
            {
                var claimed = await _context.GoodsReceipts
                    .Where(candidate =>
                        candidate.Id == receiptId &&
                        candidate.Status == DocumentStatus.Completed)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(candidate => candidate.Status, DocumentStatus.Cancelled));
                if (claimed != 1)
                {
                    await CompleteCancellationTransactionAsync(transactionScope);
                    return false;
                }
            }

            foreach (var resolvedLine in resolvedLines)
            {
                var line = resolvedLine.Line;
                var lot = resolvedLine.Lot;

                decimal qtyAfter;
                if (_context.Database.IsRelational())
                {
                    var updated = lot is null
                        ? 0
                        : await _context.StockBalances
                            .Where(balance =>
                                balance.ProductId == line.ProductId &&
                                balance.LotId == lot.Id &&
                                balance.LocationId == line.LocationId &&
                                balance.QtyAvailable >= line.Qty)
                            .ExecuteUpdateAsync(setters => setters
                                .SetProperty(balance => balance.QtyAvailable,
                                    balance => balance.QtyAvailable - line.Qty));
                    if (updated != 1)
                    {
                        var availableQty = lot is null
                            ? null
                            : await _context.StockBalances
                                .Where(balance =>
                                    balance.ProductId == line.ProductId &&
                                    balance.LotId == lot.Id &&
                                    balance.LocationId == line.LocationId)
                                .Select(balance => (decimal?)balance.QtyAvailable)
                                .SingleOrDefaultAsync();
                        throw CreateReceiptCancellationInsufficientStockException(line.Qty, availableQty);
                    }

                    qtyAfter = await _context.StockBalances
                        .Where(balance =>
                            balance.ProductId == line.ProductId &&
                            balance.LotId == lot!.Id &&
                            balance.LocationId == line.LocationId)
                        .Select(balance => balance.QtyAvailable)
                        .SingleAsync();
                    SynchronizeTrackedBalance(
                        line.ProductId,
                        lot!.Id,
                        line.LocationId,
                        qtyAfter);

                    var updatedLot = await _context.Lots
                        .Where(candidate => candidate.Id == lot!.Id && candidate.Qty >= line.Qty)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(candidate => candidate.Qty, candidate => candidate.Qty - line.Qty));
                    if (updatedLot != 1)
                    {
                        throw CreateReceiptCancellationInsufficientStockException(line.Qty, qtyAfter + line.Qty);
                    }

                    var lotQtyAfter = await _context.Lots
                        .Where(candidate => candidate.Id == lot.Id)
                        .Select(candidate => candidate.Qty)
                        .SingleAsync();
                    SynchronizeTrackedLot(lot.Id, lotQtyAfter);
                }
                else
                {
                    var trackedLot = lot is null
                        ? null
                        : await _context.Lots.FindAsync(lot.Id);
                    var balance = trackedLot is null
                        ? null
                        : await _context.StockBalances.FirstOrDefaultAsync(candidate =>
                            candidate.ProductId == line.ProductId &&
                            candidate.LotId == trackedLot.Id &&
                            candidate.LocationId == line.LocationId);
                    if (balance is null ||
                        balance.QtyAvailable < line.Qty ||
                        trackedLot!.Qty < line.Qty)
                    {
                        throw CreateReceiptCancellationInsufficientStockException(
                            line.Qty,
                            balance?.QtyAvailable);
                    }

                    balance.QtyAvailable -= line.Qty;
                    trackedLot.Qty -= line.Qty;
                    qtyAfter = balance.QtyAvailable;
                }

                await _context.StockTransactions.AddAsync(new StockTransaction
                {
                    Type = TransactionType.Receipt,
                    ProductId = line.ProductId,
                    LotId = lot!.Id,
                    LocationId = line.LocationId,
                    Qty = -line.Qty,
                    QtyAfter = qtyAfter,
                    ValuationRate = lot.UnitPrice,
                    IsCancelled = true,
                    TransactionDate = DateTime.UtcNow,
                    UserId = userId,
                    ReferenceNo = receipt.ReceiptNo
                });
            }

            receipt.Status = DocumentStatus.Cancelled;
            await _context.SaveChangesAsync();
            await CompleteCancellationTransactionAsync(transactionScope);
            return true;
        }
        catch
        {
            await RollbackCancellationTransactionAsync(transactionScope);
            RestoreChangeTracker(changeTrackerSnapshot);
            throw;
        }
    }

    public async Task<bool> CancelGoodsIssueAsync(int issueId, string userId)
    {
        var changeTrackerSnapshot = CaptureChangeTracker();
        var transactionScope = await BeginCancellationTransactionAsync("CancelGoodsIssue");
        await using var ownedTransaction = transactionScope.OwnedTransaction;
        try
        {
            var issue = await _context.GoodsIssues
                .Include(issue => issue.Lines)
                .FirstOrDefaultAsync(issue => issue.Id == issueId);

            if (issue is null || issue.Status != DocumentStatus.Completed)
            {
                await CompleteCancellationTransactionAsync(transactionScope);
                return false;
            }

            if (issue.Lines.Any(line => line.Qty <= 0))
            {
                throw new InvalidOperationException("Quantity must be greater than zero.");
            }

            EnsureIssueCancellationTargetsAreClean(issue.Lines);

            if (_context.Database.IsRelational())
            {
                var claimed = await _context.GoodsIssues
                    .Where(candidate =>
                        candidate.Id == issueId &&
                        candidate.Status == DocumentStatus.Completed)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(candidate => candidate.Status, DocumentStatus.Cancelled));
                if (claimed != 1)
                {
                    await CompleteCancellationTransactionAsync(transactionScope);
                    return false;
                }
            }

            var lockedBalances =
                new Dictionary<(int ProductId, int LotId, int LocationId), StockBalance>();

            foreach (var line in issue.Lines
                .OrderBy(line => line.ProductId)
                .ThenBy(line => line.LotId)
                .ThenBy(line => line.LocationId)
                .ThenBy(line => line.Id))
            {
                decimal qtyAfter;
                if (_context.Database.IsRelational())
                {
                    var balanceKey = (line.ProductId, line.LotId, line.LocationId);
                    if (!lockedBalances.TryGetValue(balanceKey, out var balance))
                    {
                        balance = await FindStockBalanceWithExclusiveAccessAsync(
                            line.ProductId,
                            line.LotId,
                            line.LocationId);
                        if (balance is null)
                        {
                            balance = new StockBalance
                            {
                                ProductId = line.ProductId,
                                LotId = line.LotId,
                                LocationId = line.LocationId,
                                QtyAvailable = 0,
                                QtyReserved = 0,
                                QtyOnHold = 0
                            };
                            await _context.StockBalances.AddAsync(balance);
                        }

                        lockedBalances.Add(balanceKey, balance);
                    }

                    balance.QtyAvailable += line.Qty;
                    qtyAfter = balance.QtyAvailable;
                }
                else
                {
                    var balance = await _context.StockBalances
                        .FirstOrDefaultAsync(candidate =>
                            candidate.ProductId == line.ProductId &&
                            candidate.LotId == line.LotId &&
                            candidate.LocationId == line.LocationId);
                    if (balance is null)
                    {
                        balance = new StockBalance
                        {
                            ProductId = line.ProductId,
                            LotId = line.LotId,
                            LocationId = line.LocationId,
                            QtyAvailable = 0,
                            QtyReserved = 0,
                            QtyOnHold = 0
                        };
                        await _context.StockBalances.AddAsync(balance);
                    }

                    balance.QtyAvailable += line.Qty;
                    qtyAfter = balance.QtyAvailable;
                }

                var lot = await _context.Lots.FindAsync(line.LotId);
                await _context.StockTransactions.AddAsync(new StockTransaction
                {
                    Type = TransactionType.Issue,
                    ProductId = line.ProductId,
                    LotId = line.LotId,
                    LocationId = line.LocationId,
                    Qty = line.Qty,
                    QtyAfter = qtyAfter,
                    ValuationRate = lot?.UnitPrice ?? 0m,
                    IsCancelled = true,
                    TransactionDate = DateTime.UtcNow,
                    UserId = userId,
                    ReferenceNo = issue.IssueNo
                });
            }

            issue.Status = DocumentStatus.Cancelled;
            await _context.SaveChangesAsync();
            await CompleteCancellationTransactionAsync(transactionScope);
            return true;
        }
        catch
        {
            await RollbackCancellationTransactionAsync(transactionScope);
            RestoreChangeTracker(changeTrackerSnapshot);
            throw;
        }
    }

    private static InvalidOperationException CreateReceiptCancellationInsufficientStockException(
        decimal requiredQty,
        decimal? availableQty) =>
        new($"Không thể hủy phiếu nhập. Số lượng khả dụng hiện tại ở vị trí đã chọn không đủ để trừ hoàn lại (Cần {requiredQty}, Hiện có {availableQty ?? 0m}).");

    private async Task<List<ResolvedReceiptCancellationLine>> ResolveReceiptCancellationLinesAsync(
        IEnumerable<GoodsReceiptLine> lines)
    {
        var resolvedLines = new List<ResolvedReceiptCancellationLine>();
        foreach (var line in lines)
        {
            var lot = await _context.Lots
                .AsNoTracking()
                .FirstOrDefaultAsync(candidate =>
                    candidate.ProductId == line.ProductId &&
                    candidate.LotNo == line.LotNo);
            resolvedLines.Add(new ResolvedReceiptCancellationLine(line, lot));
        }

        return resolvedLines
            .OrderBy(resolved => resolved.Line.ProductId)
            .ThenBy(resolved => resolved.Lot?.Id ?? int.MaxValue)
            .ThenBy(resolved => resolved.Line.LocationId)
            .ThenBy(resolved => resolved.Line.Id)
            .ToList();
    }

    private void EnsureReceiptCancellationTargetsAreClean(
        IEnumerable<ResolvedReceiptCancellationLine> resolvedLines)
    {
        _context.ChangeTracker.DetectChanges();

        foreach (var resolvedLine in resolvedLines)
        {
            if (resolvedLine.Lot is null)
            {
                continue;
            }

            var trackedLot = _context.ChangeTracker.Entries<Lot>()
                .FirstOrDefault(entry => entry.Entity.Id == resolvedLine.Lot.Id);
            if (trackedLot is not null && trackedLot.State != EntityState.Unchanged)
            {
                throw CreateDirtyCancellationTargetException();
            }

            var trackedBalance = _context.ChangeTracker.Entries<StockBalance>()
                .FirstOrDefault(entry =>
                    entry.Entity.ProductId == resolvedLine.Line.ProductId &&
                    entry.Entity.LotId == resolvedLine.Lot.Id &&
                    entry.Entity.LocationId == resolvedLine.Line.LocationId);
            if (trackedBalance is not null && trackedBalance.State != EntityState.Unchanged)
            {
                throw CreateDirtyCancellationTargetException();
            }
        }
    }

    private void EnsureIssueCancellationTargetsAreClean(IEnumerable<GoodsIssueLine> lines)
    {
        _context.ChangeTracker.DetectChanges();

        foreach (var line in lines)
        {
            var trackedLot = _context.ChangeTracker.Entries<Lot>()
                .FirstOrDefault(entry => entry.Entity.Id == line.LotId);
            if (trackedLot is not null && trackedLot.State != EntityState.Unchanged)
            {
                throw CreateDirtyCancellationTargetException();
            }

            var trackedBalance = _context.ChangeTracker.Entries<StockBalance>()
                .FirstOrDefault(entry =>
                    entry.Entity.ProductId == line.ProductId &&
                    entry.Entity.LotId == line.LotId &&
                    entry.Entity.LocationId == line.LocationId);
            if (trackedBalance is not null && trackedBalance.State != EntityState.Unchanged)
            {
                throw CreateDirtyCancellationTargetException();
            }
        }
    }

    private static InvalidOperationException CreateDirtyCancellationTargetException() =>
        new("Cannot cancel an inventory document while an affected lot or stock balance has unsaved changes.");

    private async Task<StockBalance?> FindStockBalanceWithExclusiveAccessAsync(
        int productId,
        int lotId,
        int locationId)
    {
        var pendingBalance = _context.ChangeTracker.Entries<StockBalance>()
            .FirstOrDefault(entry =>
                entry.State == EntityState.Added &&
                entry.Entity.ProductId == productId &&
                entry.Entity.LotId == lotId &&
                entry.Entity.LocationId == locationId)
            ?.Entity;
        if (pendingBalance is not null)
        {
            return pendingBalance;
        }

        if (_context.Database.ProviderName == "Microsoft.EntityFrameworkCore.SqlServer")
        {
            var databaseBalance = await CreateSqlServerLockedBalanceQuery(
                    productId,
                    lotId,
                    locationId)
                .AsNoTracking()
                .SingleOrDefaultAsync();
            return TrackFreshBalance(databaseBalance, productId, lotId, locationId);
        }

        var relationalBalance = await _context.StockBalances
            .AsNoTracking()
            .SingleOrDefaultAsync(balance =>
                balance.ProductId == productId &&
                balance.LotId == lotId &&
                balance.LocationId == locationId);
        return TrackFreshBalance(relationalBalance, productId, lotId, locationId);
    }

    private IQueryable<StockBalance> CreateSqlServerLockedBalanceQuery(
        int productId,
        int lotId,
        int locationId) =>
        _context.StockBalances.FromSqlInterpolated($"""
            SELECT *
            FROM [StockBalances] WITH (UPDLOCK, HOLDLOCK)
            WHERE [ProductId] = {productId}
              AND [LotId] = {lotId}
              AND [LocationId] = {locationId}
            """);

    private StockBalance? TrackFreshBalance(
        StockBalance? databaseBalance,
        int productId,
        int lotId,
        int locationId)
    {
        if (databaseBalance is null)
        {
            return null;
        }

        var trackedEntry = _context.ChangeTracker.Entries<StockBalance>()
            .FirstOrDefault(entry =>
                entry.Entity.ProductId == productId &&
                entry.Entity.LotId == lotId &&
                entry.Entity.LocationId == locationId);
        if (trackedEntry is null)
        {
            _context.StockBalances.Attach(databaseBalance);
            return databaseBalance;
        }

        trackedEntry.CurrentValues.SetValues(databaseBalance);
        trackedEntry.OriginalValues.SetValues(databaseBalance);
        trackedEntry.State = EntityState.Unchanged;
        return trackedEntry.Entity;
    }

    private async Task<CancellationTransactionScope> BeginCancellationTransactionAsync(
        string savepointName)
    {
        if (!_context.Database.IsRelational())
        {
            return new CancellationTransactionScope(null, null, null);
        }

        var ambientTransaction = _context.Database.CurrentTransaction;
        if (ambientTransaction is null)
        {
            var ownedTransaction = await _context.Database
                .BeginTransactionAsync(IsolationLevel.Serializable);
            return new CancellationTransactionScope(ownedTransaction, null, null);
        }

        if (!ambientTransaction.SupportsSavepoints)
        {
            throw new InvalidOperationException(
                "Cancellation inside an ambient transaction requires savepoint support.");
        }

        await ambientTransaction.CreateSavepointAsync(savepointName);
        return new CancellationTransactionScope(null, ambientTransaction, savepointName);
    }

    private static async Task CompleteCancellationTransactionAsync(
        CancellationTransactionScope transactionScope)
    {
        if (transactionScope.OwnedTransaction is not null)
        {
            await transactionScope.OwnedTransaction.CommitAsync();
        }
        else if (transactionScope.AmbientTransaction is not null &&
                 transactionScope.SavepointName is not null)
        {
            await transactionScope.AmbientTransaction
                .ReleaseSavepointAsync(transactionScope.SavepointName);
        }
    }

    private static async Task RollbackCancellationTransactionAsync(
        CancellationTransactionScope transactionScope)
    {
        if (transactionScope.OwnedTransaction is not null)
        {
            await transactionScope.OwnedTransaction.RollbackAsync();
        }
        else if (transactionScope.AmbientTransaction is not null &&
                 transactionScope.SavepointName is not null)
        {
            await transactionScope.AmbientTransaction
                .RollbackToSavepointAsync(transactionScope.SavepointName);
            await transactionScope.AmbientTransaction
                .ReleaseSavepointAsync(transactionScope.SavepointName);
        }
    }

    private List<TrackedEntitySnapshot> CaptureChangeTracker() =>
        _context.ChangeTracker.Entries()
            .Select(entry => new TrackedEntitySnapshot(
                entry.Entity,
                entry.State,
                entry.CurrentValues.Clone(),
                entry.OriginalValues.Clone(),
                entry.Properties.ToDictionary(
                    property => property.Metadata.Name,
                    property => property.IsModified)))
            .ToList();

    private void RestoreChangeTracker(IEnumerable<TrackedEntitySnapshot> snapshots)
    {
        var snapshotByEntity = snapshots.ToDictionary(
            snapshot => snapshot.Entity,
            ReferenceEqualityComparer.Instance);

        foreach (var entry in _context.ChangeTracker.Entries().ToList())
        {
            if (!snapshotByEntity.TryGetValue(entry.Entity, out var snapshot))
            {
                entry.State = EntityState.Detached;
                continue;
            }

            entry.CurrentValues.SetValues(snapshot.CurrentValues);
            entry.OriginalValues.SetValues(snapshot.OriginalValues);
            entry.State = snapshot.State;
            if (snapshot.State is EntityState.Modified or EntityState.Unchanged)
            {
                foreach (var property in entry.Properties)
                {
                    property.IsModified = snapshot.ModifiedProperties[property.Metadata.Name];
                }
            }
        }
    }

    private void SynchronizeTrackedBalance(
        int productId,
        int lotId,
        int locationId,
        decimal qtyAvailable)
    {
        var entry = _context.ChangeTracker.Entries<StockBalance>()
            .FirstOrDefault(candidate =>
                candidate.Entity.ProductId == productId &&
                candidate.Entity.LotId == lotId &&
                candidate.Entity.LocationId == locationId);
        if (entry is null)
        {
            return;
        }

        var property = entry.Property(balance => balance.QtyAvailable);
        property.CurrentValue = qtyAvailable;
        property.OriginalValue = qtyAvailable;
        property.IsModified = false;
    }

    private void SynchronizeTrackedLot(int lotId, decimal qty)
    {
        var entry = _context.ChangeTracker.Entries<Lot>()
            .FirstOrDefault(candidate => candidate.Entity.Id == lotId);
        if (entry is null)
        {
            return;
        }

        var property = entry.Property(lot => lot.Qty);
        property.CurrentValue = qty;
        property.OriginalValue = qty;
        property.IsModified = false;
    }

    private sealed record TrackedEntitySnapshot(
        object Entity,
        EntityState State,
        PropertyValues CurrentValues,
        PropertyValues OriginalValues,
        IReadOnlyDictionary<string, bool> ModifiedProperties);

    private sealed record CancellationTransactionScope(
        IDbContextTransaction? OwnedTransaction,
        IDbContextTransaction? AmbientTransaction,
        string? SavepointName);

    private sealed record ResolvedReceiptCancellationLine(
        GoodsReceiptLine Line,
        Lot? Lot);

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

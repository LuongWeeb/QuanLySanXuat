using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
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
        EnsureTrackedCompletionDocumentIsClean<GoodsReceipt>(
            receipt => receipt.Id == receiptId);
        var changeTrackerSnapshot = CaptureChangeTracker();
        var transactionScope = await BeginCompletionTransactionAsync(
            CreateCompletionSavepointName());
        await using var ownedTransaction = transactionScope.OwnedTransaction;
        try
        {
            if (_context.Database.IsRelational())
            {
                var claimed = await _context.GoodsReceipts
                    .Where(candidate =>
                        candidate.Id == receiptId &&
                        candidate.Status == DocumentStatus.Draft)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(candidate => candidate.Status, DocumentStatus.Completed));
                if (claimed != 1)
                {
                    await CompleteCompletionTransactionAsync(transactionScope);
                    return false;
                }
            }

            var receipt = await _context.GoodsReceipts
                .AsNoTracking()
                .Include(r => r.Lines)
                .FirstOrDefaultAsync(r => r.Id == receiptId);

            if (receipt is null ||
                (!_context.Database.IsRelational() &&
                 receipt.Status != DocumentStatus.Draft))
            {
                await CompleteCompletionTransactionAsync(transactionScope);
                return false;
            }

            if (receipt.Lines.Any(line => line.Qty <= 0))
            {
                throw new InvalidOperationException("Quantity must be greater than zero.");
            }

            var resolvedLots = new Dictionary<(int ProductId, string CanonicalLotNo), Lot?>();
            foreach (var line in receipt.Lines
                .GroupBy(line => GetLotCacheKey(line.ProductId, line.LotNo))
                .OrderBy(group => group.Key.ProductId)
                .ThenBy(group => group.Key.CanonicalLotNo, StringComparer.Ordinal)
                .Select(group => group.First()))
            {
                var key = GetLotCacheKey(line.ProductId, line.LotNo);
                resolvedLots[key] = _context.Database.ProviderName ==
                    "Microsoft.EntityFrameworkCore.SqlServer"
                    ? await CreateSqlServerReceiptLotResolutionQuery(line.ProductId, line.LotNo)
                        .AsNoTracking()
                        .SingleOrDefaultAsync()
                    : await _context.Lots
                        .AsNoTracking()
                        .FirstOrDefaultAsync(lot =>
                            lot.LotNo == line.LotNo &&
                            lot.ProductId == line.ProductId);
            }

            EnsureReceiptCompletionTargetsAreClean(receipt.Lines, resolvedLots);

            var acquiredLots =
                new Dictionary<(int ProductId, string CanonicalLotNo), Lot>();
            var acquiredBalances =
                new Dictionary<(int ProductId, int LotId, int LocationId), StockBalance>();
            foreach (var line in receipt.Lines
                .OrderBy(line => line.ProductId)
                .ThenBy(line => GetCanonicalLotNo(line.LotNo), StringComparer.Ordinal)
                .ThenBy(line => line.LocationId)
                .ThenBy(line => line.Id))
            {
                var lotKey = GetLotCacheKey(line.ProductId, line.LotNo);
                var isNewLot = false;
                if (!acquiredLots.TryGetValue(lotKey, out var lot))
                {
                    var lookupLotNo = resolvedLots[lotKey]?.LotNo ?? line.LotNo;
                    var databaseLot = _context.Database.ProviderName ==
                        "Microsoft.EntityFrameworkCore.SqlServer"
                        ? await CreateSqlServerLockedLotByNaturalKeyQuery(
                                line.ProductId,
                                lookupLotNo)
                            .AsNoTracking()
                            .SingleOrDefaultAsync()
                        : await _context.Lots
                            .AsNoTracking()
                            .FirstOrDefaultAsync(candidate =>
                                candidate.LotNo == lookupLotNo &&
                                candidate.ProductId == line.ProductId);
                    lot = databaseLot is null ? null : TrackFreshLot(databaseLot);

                    if (lot is null)
                    {
                        isNewLot = true;
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

                    resolvedLots[lotKey] = lot;
                    acquiredLots.Add(lotKey, lot);
                }

                if (!isNewLot)
                {
                    var totalQty = lot.Qty + line.Qty;
                    lot.UnitPrice = totalQty > 0
                        ? Math.Round(((lot.Qty * lot.UnitPrice) + (line.Qty * line.UnitPrice)) / totalQty, 2, MidpointRounding.AwayFromZero)
                        : line.UnitPrice;
                    lot.Qty += line.Qty;
                }

                var balanceKey = (line.ProductId, lot.Id, line.LocationId);
                if (!acquiredBalances.TryGetValue(balanceKey, out var balance))
                {
                    balance = await FindStockBalanceWithExclusiveAccessAsync(
                        line.ProductId,
                        lot.Id,
                        line.LocationId);

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

                    acquiredBalances.Add(balanceKey, balance);
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

            if (receipt.PurchaseOrderId.HasValue)
            {
                var purchaseOrder = await _context.PurchaseOrders
                    .Include(order => order.Items)
                    .FirstOrDefaultAsync(order =>
                        order.Id == receipt.PurchaseOrderId.Value);
                if (purchaseOrder is not null)
                {
                    var orderItems = purchaseOrder.Items
                        .GroupBy(item => item.ProductId)
                        .ToDictionary(group => group.Key, group =>
                            group.Count() == 1
                                ? group.Single()
                                : throw new InvalidOperationException(
                                    "A purchase order cannot contain duplicate product lines."));
                    foreach (var lineGroup in receipt.Lines.GroupBy(line => line.ProductId))
                    {
                        if (!orderItems.TryGetValue(lineGroup.Key, out var orderItem))
                        {
                            throw new InvalidOperationException(
                                "The receipt contains a product that is not on the linked purchase order.");
                        }

                        var receivedQty = lineGroup.Sum(line => line.Qty);
                        if (receivedQty > orderItem.Qty - orderItem.ReceivedQty)
                        {
                            throw new InvalidOperationException(
                                "The receipt quantity exceeds the remaining purchase order quantity.");
                        }

                        orderItem.ReceivedQty += receivedQty;
                    }

                    if (purchaseOrder.Items.Count > 0 &&
                        purchaseOrder.Items.All(item => item.ReceivedQty >= item.Qty))
                    {
                        purchaseOrder.Status = DocumentStatus.Completed;
                    }
                }
            }

            if (!_context.Database.IsRelational())
            {
                await SetGoodsReceiptStatusAsync(receipt.Id, DocumentStatus.Completed);
            }
            await _context.SaveChangesAsync();
            await CompleteCompletionTransactionAsync(transactionScope);
            SynchronizeTrackedGoodsReceiptStatus(receipt.Id, DocumentStatus.Completed);
        }
        catch
        {
            await RollbackCompletionTransactionAsync(transactionScope);
            RestoreChangeTracker(changeTrackerSnapshot);
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
        EnsureTrackedCompletionDocumentIsClean<GoodsIssue>(
            issue => issue.Id == issueId);
        var changeTrackerSnapshot = CaptureChangeTracker();
        var transactionScope = await BeginCompletionTransactionAsync(
            CreateCompletionSavepointName());
        await using var ownedTransaction = transactionScope.OwnedTransaction;
        try
        {
            if (_context.Database.IsRelational())
            {
                var claimed = await _context.GoodsIssues
                    .Where(candidate =>
                        candidate.Id == issueId &&
                        candidate.Status == DocumentStatus.Draft)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(candidate => candidate.Status, DocumentStatus.Completed));
                if (claimed != 1)
                {
                    await CompleteCompletionTransactionAsync(transactionScope);
                    return false;
                }
            }

            var issue = await _context.GoodsIssues
                .AsNoTracking()
                .Include(i => i.Lines)
                .FirstOrDefaultAsync(i => i.Id == issueId);

            if (issue is null ||
                (!_context.Database.IsRelational() &&
                 issue.Status != DocumentStatus.Draft))
            {
                await CompleteCompletionTransactionAsync(transactionScope);
                return false;
            }

            if (issue.Lines.Any(line => line.Qty <= 0))
            {
                throw new InvalidOperationException("Quantity must be greater than zero.");
            }

            var resolvedLots = new Dictionary<int, Lot>();
            foreach (var lotId in issue.Lines
                .Select(line => line.LotId)
                .Distinct()
                .OrderBy(lotId => lotId))
            {
                var lotQuery = _context.Database.ProviderName ==
                    "Microsoft.EntityFrameworkCore.SqlServer"
                    ? CreateSqlServerLotResolutionByIdQuery(lotId)
                    : _context.Lots;
                var lot = await lotQuery
                    .AsNoTracking()
                    .SingleOrDefaultAsync(candidate => candidate.Id == lotId)
                    ?? throw new InvalidOperationException("The issue references a lot that no longer exists.");
                resolvedLots.Add(lotId, lot);
            }

            EnsureIssueCompletionTargetsAreClean(issue.Lines);

            var valuationRates = new Dictionary<int, decimal>();
            foreach (var lotKey in issue.Lines
                .Select(line => new
                {
                    line.ProductId,
                    Lot = resolvedLots[line.LotId]
                })
                .DistinctBy(key => key.Lot.Id)
                .OrderBy(key => key.ProductId)
                .ThenBy(key => GetCanonicalLotNo(key.Lot.LotNo), StringComparer.Ordinal))
            {
                var lotQuery = _context.Database.ProviderName ==
                    "Microsoft.EntityFrameworkCore.SqlServer"
                    ? CreateSqlServerLockedLotByNaturalKeyQuery(
                        lotKey.Lot.ProductId,
                        lotKey.Lot.LotNo)
                    : _context.Lots;
                var valuationRate = await lotQuery
                    .AsNoTracking()
                    .Where(lot => lot.Id == lotKey.Lot.Id)
                    .Select(lot => (decimal?)lot.UnitPrice)
                    .SingleOrDefaultAsync();
                valuationRates[lotKey.Lot.Id] = valuationRate
                    ?? throw new InvalidOperationException(
                        "The issue valuation lot no longer exists.");
            }

            foreach (var line in issue.Lines
                .OrderBy(line => line.ProductId)
                .ThenBy(line => GetCanonicalLotNo(resolvedLots[line.LotId].LotNo), StringComparer.Ordinal)
                .ThenBy(line => line.LocationId)
                .ThenBy(line => line.Id))
            {
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

                await _context.StockTransactions.AddAsync(new StockTransaction
                {
                    Type = TransactionType.Issue,
                    ProductId = line.ProductId,
                    LotId = line.LotId,
                    LocationId = line.LocationId,
                    Qty = -line.Qty,
                    QtyAfter = qtyAfter,
                    ValuationRate = valuationRates[line.LotId],
                    IsCancelled = false,
                    TransactionDate = DateTime.UtcNow,
                    UserId = userId,
                    ReferenceNo = issue.IssueNo
                });
            }

            if (issue.SalesOrderId.HasValue)
            {
                var salesOrder = await _context.SalesOrders
                    .Include(order => order.Items)
                    .FirstOrDefaultAsync(order =>
                        order.Id == issue.SalesOrderId.Value);
                if (salesOrder is not null)
                {
                    var orderItems = salesOrder.Items
                        .GroupBy(item => item.ProductId)
                        .ToDictionary(group => group.Key, group =>
                            group.Count() == 1
                                ? group.Single()
                                : throw new InvalidOperationException(
                                    "A sales order cannot contain duplicate product lines."));
                    foreach (var lineGroup in issue.Lines.GroupBy(line => line.ProductId))
                    {
                        if (!orderItems.TryGetValue(lineGroup.Key, out var orderItem))
                        {
                            throw new InvalidOperationException(
                                "The issue contains a product that is not on the linked sales order.");
                        }

                        var deliveredQty = lineGroup.Sum(line => line.Qty);
                        if (deliveredQty > orderItem.Qty - orderItem.DeliveredQty)
                        {
                            throw new InvalidOperationException(
                                "The issue quantity exceeds the remaining sales order quantity.");
                        }

                        orderItem.DeliveredQty += deliveredQty;
                    }

                    if (salesOrder.Items.Count > 0 &&
                        salesOrder.Items.All(item => item.DeliveredQty >= item.Qty))
                    {
                        salesOrder.Status = DocumentStatus.Completed;
                    }
                }
            }

            if (!_context.Database.IsRelational())
            {
                await SetGoodsIssueStatusAsync(issue.Id, DocumentStatus.Completed);
            }
            await _context.SaveChangesAsync();
            await CompleteCompletionTransactionAsync(transactionScope);
            SynchronizeTrackedGoodsIssueStatus(issue.Id, DocumentStatus.Completed);
        }
        catch
        {
            await RollbackCompletionTransactionAsync(transactionScope);
            RestoreChangeTracker(changeTrackerSnapshot);
            throw;
        }


        if (notify && !hasAmbientTransaction) await NotifyStockChangedSafelyAsync();
        return true;
    }

    public async Task<bool> CancelGoodsReceiptAsync(int receiptId, string userId)
    {
        EnsureTrackedCancellationDocumentIsClean<GoodsReceipt>(
            receipt => receipt.Id == receiptId);
        var changeTrackerSnapshot = CaptureChangeTracker();
        var transactionScope = await BeginCancellationTransactionAsync(
            CreateCancellationSavepointName());
        await using var ownedTransaction = transactionScope.OwnedTransaction;
        try
        {
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

            var receipt = await _context.GoodsReceipts
                .AsNoTracking()
                .Include(candidate => candidate.Lines)
                .FirstOrDefaultAsync(candidate => candidate.Id == receiptId);
            if (receipt is null ||
                (!_context.Database.IsRelational() &&
                 receipt.Status != DocumentStatus.Completed))
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

            foreach (var resolvedLine in resolvedLines)
            {
                var line = resolvedLine.Line;
                var lot = resolvedLine.Lot;

                decimal qtyAfter;
                if (_context.Database.IsRelational())
                {
                    var updatedLot = lot is null
                        ? 0
                        : await _context.Lots
                            .Where(candidate =>
                                candidate.Id == lot.Id &&
                                candidate.Qty >= line.Qty)
                            .ExecuteUpdateAsync(setters => setters
                                .SetProperty(candidate => candidate.Qty,
                                    candidate => candidate.Qty - line.Qty));
                    if (updatedLot != 1)
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

                    var lotQtyAfter = await _context.Lots
                        .Where(candidate => candidate.Id == lot!.Id)
                        .Select(candidate => candidate.Qty)
                        .SingleAsync();
                    SynchronizeTrackedLot(lot!.Id, lotQtyAfter);

                    var updatedBalance = await _context.StockBalances
                        .Where(balance =>
                            balance.ProductId == line.ProductId &&
                            balance.LotId == lot.Id &&
                            balance.LocationId == line.LocationId &&
                            balance.QtyAvailable >= line.Qty)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(balance => balance.QtyAvailable,
                                balance => balance.QtyAvailable - line.Qty));
                    if (updatedBalance != 1)
                    {
                        var availableQty = await _context.StockBalances
                            .Where(balance =>
                                balance.ProductId == line.ProductId &&
                                balance.LotId == lot.Id &&
                                balance.LocationId == line.LocationId)
                            .Select(balance => (decimal?)balance.QtyAvailable)
                            .SingleOrDefaultAsync();
                        throw CreateReceiptCancellationInsufficientStockException(
                            line.Qty,
                            availableQty);
                    }

                    qtyAfter = await _context.StockBalances
                        .Where(balance =>
                            balance.ProductId == line.ProductId &&
                            balance.LotId == lot.Id &&
                            balance.LocationId == line.LocationId)
                        .Select(balance => balance.QtyAvailable)
                        .SingleAsync();
                    SynchronizeTrackedBalance(
                        line.ProductId,
                        lot.Id,
                        line.LocationId,
                        qtyAfter);
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

            if (receipt.PurchaseOrderId.HasValue)
            {
                var purchaseOrder = await _context.PurchaseOrders
                    .Include(order => order.Items)
                    .SingleAsync(order => order.Id == receipt.PurchaseOrderId.Value);
                foreach (var lineGroup in receipt.Lines.GroupBy(line => line.ProductId))
                {
                    var orderItem = purchaseOrder.Items.Single(item =>
                        item.ProductId == lineGroup.Key);
                    orderItem.ReceivedQty -= lineGroup.Sum(line => line.Qty);
                    if (orderItem.ReceivedQty < 0m)
                    {
                        throw new InvalidOperationException(
                            "Cancelling the receipt would make the received quantity negative.");
                    }
                }
                purchaseOrder.Status = purchaseOrder.Items.All(item =>
                    item.ReceivedQty >= item.Qty)
                    ? DocumentStatus.Completed
                    : DocumentStatus.Draft;
            }

            if (!_context.Database.IsRelational())
            {
                await SetGoodsReceiptStatusAsync(receipt.Id, DocumentStatus.Cancelled);
            }
            await _context.SaveChangesAsync();
            await CompleteCancellationTransactionAsync(transactionScope);
            SynchronizeTrackedGoodsReceiptStatus(receipt.Id, DocumentStatus.Cancelled);
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
        EnsureTrackedCancellationDocumentIsClean<GoodsIssue>(
            issue => issue.Id == issueId);
        var changeTrackerSnapshot = CaptureChangeTracker();
        var transactionScope = await BeginCancellationTransactionAsync(
            CreateCancellationSavepointName());
        await using var ownedTransaction = transactionScope.OwnedTransaction;
        try
        {
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

            var issue = await _context.GoodsIssues
                .AsNoTracking()
                .Include(candidate => candidate.Lines)
                .FirstOrDefaultAsync(candidate => candidate.Id == issueId);
            if (issue is null ||
                (!_context.Database.IsRelational() &&
                 issue.Status != DocumentStatus.Completed))
            {
                await CompleteCancellationTransactionAsync(transactionScope);
                return false;
            }

            if (issue.Lines.Any(line => line.Qty <= 0))
            {
                throw new InvalidOperationException("Quantity must be greater than zero.");
            }

            var resolvedLines = await ResolveIssueCancellationLinesAsync(issue.Lines);
            if (resolvedLines.Any(resolved => resolved.Lot is null))
            {
                throw new InvalidOperationException(
                    "The issue cancellation valuation lot no longer exists.");
            }
            EnsureIssueCancellationTargetsAreClean(resolvedLines);

            var lockedBalances =
                new Dictionary<(int ProductId, int LotId, int LocationId), StockBalance>();

            foreach (var resolvedLine in resolvedLines)
            {
                var line = resolvedLine.Line;
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

                await _context.StockTransactions.AddAsync(new StockTransaction
                {
                    Type = TransactionType.Issue,
                    ProductId = line.ProductId,
                    LotId = line.LotId,
                    LocationId = line.LocationId,
                    Qty = line.Qty,
                    QtyAfter = qtyAfter,
                    ValuationRate = resolvedLine.Lot!.UnitPrice,
                    IsCancelled = true,
                    TransactionDate = DateTime.UtcNow,
                    UserId = userId,
                    ReferenceNo = issue.IssueNo
                });
            }

            if (issue.SalesOrderId.HasValue)
            {
                var salesOrder = await _context.SalesOrders
                    .Include(order => order.Items)
                    .SingleAsync(order => order.Id == issue.SalesOrderId.Value);
                foreach (var lineGroup in issue.Lines.GroupBy(line => line.ProductId))
                {
                    var orderItem = salesOrder.Items.Single(item =>
                        item.ProductId == lineGroup.Key);
                    orderItem.DeliveredQty -= lineGroup.Sum(line => line.Qty);
                    if (orderItem.DeliveredQty < 0m)
                    {
                        throw new InvalidOperationException(
                            "Cancelling the issue would make the delivered quantity negative.");
                    }
                }
                salesOrder.Status = salesOrder.Items.All(item =>
                    item.DeliveredQty >= item.Qty)
                    ? DocumentStatus.Completed
                    : DocumentStatus.Draft;
            }

            if (!_context.Database.IsRelational())
            {
                await SetGoodsIssueStatusAsync(issue.Id, DocumentStatus.Cancelled);
            }
            await _context.SaveChangesAsync();
            await CompleteCancellationTransactionAsync(transactionScope);
            SynchronizeTrackedGoodsIssueStatus(issue.Id, DocumentStatus.Cancelled);
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
            .ThenBy(
                resolved => GetCanonicalLotNo(
                    resolved.Lot?.LotNo ?? resolved.Line.LotNo),
                StringComparer.Ordinal)
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
                    EntryMatchesBalanceTarget(
                        entry,
                        resolvedLine.Line.ProductId,
                        resolvedLine.Lot.Id,
                        resolvedLine.Line.LocationId));
            if (trackedBalance is not null && trackedBalance.State != EntityState.Unchanged)
            {
                throw CreateDirtyCancellationTargetException();
            }
        }
    }

    private async Task<List<ResolvedIssueCancellationLine>> ResolveIssueCancellationLinesAsync(
        IEnumerable<GoodsIssueLine> lines)
    {
        var resolvedLines = new List<ResolvedIssueCancellationLine>();
        foreach (var line in lines)
        {
            var lot = await _context.Lots
                .AsNoTracking()
                .FirstOrDefaultAsync(candidate => candidate.Id == line.LotId);
            resolvedLines.Add(new ResolvedIssueCancellationLine(line, lot));
        }

        return resolvedLines
            .OrderBy(resolved => resolved.Line.ProductId)
            .ThenBy(
                resolved => GetCanonicalLotNo(resolved.Lot?.LotNo ?? string.Empty),
                StringComparer.Ordinal)
            .ThenBy(resolved => resolved.Line.LocationId)
            .ThenBy(resolved => resolved.Line.Id)
            .ToList();
    }

    private void EnsureIssueCancellationTargetsAreClean(
        IEnumerable<ResolvedIssueCancellationLine> resolvedLines)
    {
        _context.ChangeTracker.DetectChanges();

        foreach (var resolvedLine in resolvedLines)
        {
            var line = resolvedLine.Line;
            var trackedLot = _context.ChangeTracker.Entries<Lot>()
                .FirstOrDefault(entry => entry.Entity.Id == line.LotId);
            if (trackedLot is not null && trackedLot.State != EntityState.Unchanged)
            {
                throw CreateDirtyCancellationTargetException();
            }

            var trackedBalance = _context.ChangeTracker.Entries<StockBalance>()
                .FirstOrDefault(entry =>
                    EntryMatchesBalanceTarget(
                        entry,
                        line.ProductId,
                        line.LotId,
                        line.LocationId));
            if (trackedBalance is not null && trackedBalance.State != EntityState.Unchanged)
            {
                throw CreateDirtyCancellationTargetException();
            }
        }
    }

    private static InvalidOperationException CreateDirtyCancellationTargetException() =>
        new("Cannot cancel an inventory document while an affected lot or stock balance has unsaved changes.");

    private void EnsureTrackedCancellationDocumentIsClean<TEntity>(
        Func<TEntity, bool> predicate)
        where TEntity : class
    {
        _context.ChangeTracker.DetectChanges();
        var trackedDocument = _context.ChangeTracker.Entries<TEntity>()
            .FirstOrDefault(entry => predicate(entry.Entity));
        if (trackedDocument is not null &&
            trackedDocument.State != EntityState.Unchanged)
        {
            throw new InvalidOperationException(
                "Cannot cancel an inventory document while that document has unsaved changes.");
        }
    }

    private void EnsureTrackedCompletionDocumentIsClean<TEntity>(
        Func<TEntity, bool> predicate)
        where TEntity : class
    {
        _context.ChangeTracker.DetectChanges();
        var trackedDocument = _context.ChangeTracker.Entries<TEntity>()
            .FirstOrDefault(entry => predicate(entry.Entity));
        if (trackedDocument is not null &&
            trackedDocument.State != EntityState.Unchanged)
        {
            throw new InvalidOperationException(
                "Cannot complete an inventory document while that document has unsaved changes.");
        }
    }

    private void EnsureReceiptCompletionTargetsAreClean(
        IEnumerable<GoodsReceiptLine> lines,
        IReadOnlyDictionary<(int ProductId, string CanonicalLotNo), Lot?> resolvedLots)
    {
        _context.ChangeTracker.DetectChanges();
        var lineList = lines.ToList();
        var affectedLotKeys = lineList
            .Select(line => GetLotCacheKey(line.ProductId, line.LotNo))
            .ToHashSet();
        var resolvedLotIds = resolvedLots.Values
            .Where(lot => lot is not null)
            .Select(lot => lot!.Id)
            .ToHashSet();
        if (_context.ChangeTracker.Entries<Lot>().Any(entry =>
                (resolvedLotIds.Contains(entry.Entity.Id) ||
                 affectedLotKeys.Contains(
                     GetLotCacheKey(entry.Entity.ProductId, entry.Entity.LotNo)) ||
                 affectedLotKeys.Contains(GetOriginalLotKey(entry))) &&
                entry.State != EntityState.Unchanged))
        {
            throw CreateDirtyCompletionTargetException();
        }

        var affectedBalanceKeys = lineList
            .Select(line =>
            {
                var lot = resolvedLots[GetLotCacheKey(line.ProductId, line.LotNo)];
                return lot is null
                    ? ((int ProductId, int LotId, int LocationId)?)null
                    : (line.ProductId, lot.Id, line.LocationId);
            })
            .Where(key => key.HasValue)
            .Select(key => key!.Value)
            .ToHashSet();
        if (_context.ChangeTracker.Entries<StockBalance>().Any(entry =>
                (affectedBalanceKeys.Contains((
                     entry.Entity.ProductId,
                     entry.Entity.LotId,
                     entry.Entity.LocationId)) ||
                 affectedBalanceKeys.Contains(GetOriginalBalanceKey(entry))) &&
                entry.State != EntityState.Unchanged))
        {
            throw CreateDirtyCompletionTargetException();
        }
    }

    private void EnsureIssueCompletionTargetsAreClean(
        IEnumerable<GoodsIssueLine> lines)
    {
        _context.ChangeTracker.DetectChanges();
        var lineList = lines.ToList();
        var affectedLotIds = lineList
            .Select(line => line.LotId)
            .ToHashSet();
        if (_context.ChangeTracker.Entries<Lot>().Any(entry =>
                affectedLotIds.Contains(entry.Entity.Id) &&
                entry.State != EntityState.Unchanged))
        {
            throw CreateDirtyCompletionTargetException();
        }

        var affectedBalanceKeys = lineList
            .Select(line => (line.ProductId, line.LotId, line.LocationId))
            .ToHashSet();
        if (_context.ChangeTracker.Entries<StockBalance>().Any(entry =>
                (affectedBalanceKeys.Contains((
                     entry.Entity.ProductId,
                     entry.Entity.LotId,
                     entry.Entity.LocationId)) ||
                 affectedBalanceKeys.Contains(GetOriginalBalanceKey(entry))) &&
                entry.State != EntityState.Unchanged))
        {
            throw CreateDirtyCompletionTargetException();
        }
    }

    private static InvalidOperationException CreateDirtyCompletionTargetException() =>
        new("Cannot complete an inventory document while an affected lot or stock balance has unsaved changes.");

    private static (int ProductId, string CanonicalLotNo) GetLotCacheKey(
        int productId,
        string lotNo) =>
        (productId, GetCanonicalLotNo(lotNo));

    private static string GetCanonicalLotNo(string lotNo) =>
        lotNo.Trim().ToUpperInvariant();

    private static bool EntryMatchesBalanceTarget(
        EntityEntry<StockBalance> entry,
        int productId,
        int lotId,
        int locationId) =>
        (entry.Entity.ProductId == productId &&
         entry.Entity.LotId == lotId &&
         entry.Entity.LocationId == locationId) ||
        GetOriginalBalanceKey(entry) == (productId, lotId, locationId);

    private static (int ProductId, int LotId, int LocationId) GetOriginalBalanceKey(
        EntityEntry<StockBalance> entry) =>
        (
            entry.OriginalValues.GetValue<int>(nameof(StockBalance.ProductId)),
            entry.OriginalValues.GetValue<int>(nameof(StockBalance.LotId)),
            entry.OriginalValues.GetValue<int>(nameof(StockBalance.LocationId))
        );

    private static (int ProductId, string CanonicalLotNo) GetOriginalLotKey(
        EntityEntry<Lot> entry) =>
        GetLotCacheKey(
            entry.OriginalValues.GetValue<int>(nameof(Lot.ProductId)),
            entry.OriginalValues.GetValue<string>(nameof(Lot.LotNo)));

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

    private IQueryable<Lot> CreateSqlServerReceiptLotResolutionQuery(
        int productId,
        string lotNo) =>
        _context.Lots.FromSqlInterpolated($"""
            SELECT *
            FROM [Lots] WITH (READCOMMITTEDLOCK)
            WHERE [ProductId] = {productId}
              AND [LotNo] = {lotNo}
            """);

    private IQueryable<Lot> CreateSqlServerLotResolutionByIdQuery(int lotId) =>
        _context.Lots.FromSqlInterpolated($"""
            SELECT *
            FROM [Lots] WITH (READCOMMITTEDLOCK)
            WHERE [Id] = {lotId}
            """);

    private IQueryable<Lot> CreateSqlServerLockedLotByNaturalKeyQuery(
        int productId,
        string lotNo) =>
        _context.Lots.FromSqlInterpolated($"""
            SELECT *
            FROM [Lots] WITH (UPDLOCK, HOLDLOCK)
            WHERE [ProductId] = {productId}
              AND [LotNo] = {lotNo}
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

    private Lot TrackFreshLot(Lot databaseLot)
    {
        var trackedEntry = _context.ChangeTracker.Entries<Lot>()
            .FirstOrDefault(entry => entry.Entity.Id == databaseLot.Id);
        if (trackedEntry is null)
        {
            _context.Lots.Attach(databaseLot);
            return databaseLot;
        }

        trackedEntry.CurrentValues.SetValues(databaseLot);
        trackedEntry.OriginalValues.SetValues(databaseLot);
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
            var ownedTransaction = await _context.Database.BeginTransactionAsync();
            return new CancellationTransactionScope(ownedTransaction, null, null);
        }

        if (_context.Database.ProviderName == "Microsoft.EntityFrameworkCore.SqlServer" &&
            IsUnsupportedSqlServerAmbientIsolation(
                ambientTransaction.GetDbTransaction().IsolationLevel))
        {
            throw new InvalidOperationException(
                "SQL Server cancellation requires an ambient transaction using ReadCommitted isolation.");
        }

        if (!ambientTransaction.SupportsSavepoints)
        {
            throw new InvalidOperationException(
                "Cancellation inside an ambient transaction requires savepoint support.");
        }

        await ambientTransaction.CreateSavepointAsync(savepointName);
        return new CancellationTransactionScope(null, ambientTransaction, savepointName);
    }

    private async Task<CompletionTransactionScope> BeginCompletionTransactionAsync(
        string savepointName)
    {
        if (!_context.Database.IsRelational())
        {
            return new CompletionTransactionScope(null, null, null);
        }

        var ambientTransaction = _context.Database.CurrentTransaction;
        if (ambientTransaction is null)
        {
            var ownedTransaction = await _context.Database.BeginTransactionAsync();
            return new CompletionTransactionScope(ownedTransaction, null, null);
        }

        if (!ambientTransaction.SupportsSavepoints)
        {
            throw new InvalidOperationException(
                "Completion inside an ambient transaction requires savepoint support.");
        }

        await ambientTransaction.CreateSavepointAsync(savepointName);
        return new CompletionTransactionScope(
            null,
            ambientTransaction,
            savepointName);
    }

    private static string CreateCompletionSavepointName() =>
        Guid.NewGuid().ToString("N");

    private static string CreateCancellationSavepointName() =>
        Guid.NewGuid().ToString("N");

    private static bool IsUnsupportedSqlServerAmbientIsolation(
        System.Data.IsolationLevel isolationLevel) =>
        isolationLevel is not System.Data.IsolationLevel.ReadCommitted;

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

    private static async Task CompleteCompletionTransactionAsync(
        CompletionTransactionScope transactionScope)
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

    private static async Task RollbackCompletionTransactionAsync(
        CompletionTransactionScope transactionScope)
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
                    property => property.IsModified),
                entry.Properties.ToDictionary(
                    property => property.Metadata.Name,
                    property => property.IsTemporary))
                with
                {
                    ClrPropertyValues = entry.Properties.ToDictionary(
                        property => property.Metadata.Name,
                        property => property.Metadata.PropertyInfo is not null
                            ? property.Metadata.PropertyInfo.GetValue(entry.Entity)
                            : property.Metadata.FieldInfo?.GetValue(entry.Entity))
                })
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
            }
        }

        foreach (var snapshot in snapshotByEntity.Values)
        {
            var entry = _context.Entry(snapshot.Entity);
            var restoredAcceptedAddedEntry = false;
            if (snapshot.State == EntityState.Added &&
                entry.State != EntityState.Added)
            {
                entry.State = EntityState.Detached;
                foreach (var property in entry.Properties)
                {
                    var propertyName = property.Metadata.Name;
                    if (!snapshot.TemporaryProperties[propertyName])
                    {
                        property.CurrentValue =
                            snapshot.CurrentValues[propertyName];
                        continue;
                    }

                    if (property.Metadata.PropertyInfo is not null)
                    {
                        property.Metadata.PropertyInfo.SetValue(
                            snapshot.Entity,
                            snapshot.ClrPropertyValues[propertyName]);
                    }
                    else if (property.Metadata.FieldInfo is not null)
                    {
                        property.Metadata.FieldInfo.SetValue(
                            snapshot.Entity,
                            snapshot.ClrPropertyValues[propertyName]);
                    }
                }

                entry.State = EntityState.Added;
                restoredAcceptedAddedEntry = true;
            }

            if (!restoredAcceptedAddedEntry &&
                entry.State == EntityState.Detached)
            {
                entry.CurrentValues.SetValues(snapshot.CurrentValues);
                entry.State = snapshot.State == EntityState.Deleted
                    ? EntityState.Unchanged
                    : snapshot.State;
            }
            else if (!restoredAcceptedAddedEntry)
            {
                entry.CurrentValues.SetValues(snapshot.CurrentValues);
                entry.State = snapshot.State;
            }

            entry.OriginalValues.SetValues(snapshot.OriginalValues);
            if (snapshot.State == EntityState.Deleted)
            {
                entry.State = EntityState.Deleted;
            }

            foreach (var property in entry.Properties)
            {
                property.IsTemporary =
                    snapshot.TemporaryProperties[property.Metadata.Name];
            }

            if (snapshot.State is EntityState.Modified or EntityState.Unchanged)
            {
                foreach (var property in entry.Properties)
                {
                    property.IsModified = snapshot.ModifiedProperties[property.Metadata.Name];
                }
            }

            foreach (var property in entry.Properties.Where(property =>
                         snapshot.TemporaryProperties[property.Metadata.Name]))
            {
                var propertyName = property.Metadata.Name;
                if (property.Metadata.PropertyInfo is not null)
                {
                    property.Metadata.PropertyInfo.SetValue(
                        snapshot.Entity,
                        snapshot.ClrPropertyValues[propertyName]);
                }
                else if (property.Metadata.FieldInfo is not null)
                {
                    property.Metadata.FieldInfo.SetValue(
                        snapshot.Entity,
                        snapshot.ClrPropertyValues[propertyName]);
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

    private async Task SetGoodsReceiptStatusAsync(int receiptId, DocumentStatus status)
    {
        var receipt = _context.ChangeTracker.Entries<GoodsReceipt>()
            .FirstOrDefault(entry => entry.Entity.Id == receiptId)
            ?.Entity
            ?? await _context.GoodsReceipts.FindAsync(receiptId);
        if (receipt is not null)
        {
            receipt.Status = status;
        }
    }

    private async Task SetGoodsIssueStatusAsync(int issueId, DocumentStatus status)
    {
        var issue = _context.ChangeTracker.Entries<GoodsIssue>()
            .FirstOrDefault(entry => entry.Entity.Id == issueId)
            ?.Entity
            ?? await _context.GoodsIssues.FindAsync(issueId);
        if (issue is not null)
        {
            issue.Status = status;
        }
    }

    private void SynchronizeTrackedGoodsReceiptStatus(int receiptId, DocumentStatus status)
    {
        var entry = _context.ChangeTracker.Entries<GoodsReceipt>()
            .FirstOrDefault(candidate => candidate.Entity.Id == receiptId);
        if (entry is null)
        {
            return;
        }

        var property = entry.Property(receipt => receipt.Status);
        property.CurrentValue = status;
        property.OriginalValue = status;
        property.IsModified = false;
    }

    private void SynchronizeTrackedGoodsIssueStatus(int issueId, DocumentStatus status)
    {
        var entry = _context.ChangeTracker.Entries<GoodsIssue>()
            .FirstOrDefault(candidate => candidate.Entity.Id == issueId);
        if (entry is null)
        {
            return;
        }

        var property = entry.Property(issue => issue.Status);
        property.CurrentValue = status;
        property.OriginalValue = status;
        property.IsModified = false;
    }

    private sealed record TrackedEntitySnapshot(
        object Entity,
        EntityState State,
        PropertyValues CurrentValues,
        PropertyValues OriginalValues,
        IReadOnlyDictionary<string, bool> ModifiedProperties,
        IReadOnlyDictionary<string, bool> TemporaryProperties)
    {
        public IReadOnlyDictionary<string, object?> ClrPropertyValues { get; init; } =
            new Dictionary<string, object?>();
    }

    private sealed record CancellationTransactionScope(
        IDbContextTransaction? OwnedTransaction,
        IDbContextTransaction? AmbientTransaction,
        string? SavepointName);

    private sealed record CompletionTransactionScope(
        IDbContextTransaction? OwnedTransaction,
        IDbContextTransaction? AmbientTransaction,
        string? SavepointName);

    private sealed record ResolvedReceiptCancellationLine(
        GoodsReceiptLine Line,
        Lot? Lot);

    private sealed record ResolvedIssueCancellationLine(
        GoodsIssueLine Line,
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
                    .ThenInclude(line => line.Lot)
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
                        QtyAfter = balance.QtyAvailable,
                        ValuationRate = line.Lot?.UnitPrice
                            ?? throw new InvalidOperationException(
                                "The stocktake lot no longer exists."),
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
            decimal qtyAfter;
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

                qtyAfter = await _context.StockBalances
                    .Where(stockBalance =>
                        stockBalance.ProductId == productId &&
                        stockBalance.LotId == lotId &&
                        stockBalance.LocationId == locationId)
                    .Select(stockBalance => stockBalance.QtyAvailable)
                    .SingleAsync();
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
                qtyAfter = balance.QtyAvailable;
            }

            var valuationRate = await _context.Lots
                .AsNoTracking()
                .Where(lot => lot.Id == lotId)
                .Select(lot => (decimal?)lot.UnitPrice)
                .SingleOrDefaultAsync()
                ?? throw new InvalidOperationException(
                    "The adjustment valuation lot no longer exists.");
            await _context.StockTransactions.AddAsync(new StockTransaction
            {
                Type = TransactionType.Adjust,
                ProductId = productId,
                LotId = lotId,
                LocationId = locationId,
                Qty = adjustmentQty,
                QtyAfter = qtyAfter,
                ValuationRate = valuationRate,
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

using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Domain.Enums;
using WmsMes.Web.Hubs;

namespace WmsMes.Web.Services;

public class QcService : IQcService
{
    public const string QuarantineLocationCode = "QC-QUARANTINE";

    private readonly ApplicationDbContext _context;
    private readonly ICostingService _costingService;
    private readonly IHubContext<QualityHub>? _qualityHub;
    private readonly IHubContext<InventoryHub>? _inventoryHub;

    public QcService(ApplicationDbContext context, ICostingService costingService)
        : this(context, costingService, null, null)
    {
    }

    public QcService(
        ApplicationDbContext context,
        ICostingService costingService,
        IHubContext<QualityHub>? qualityHub,
        IHubContext<InventoryHub>? inventoryHub = null)
    {
        _context = context;
        _costingService = costingService;
        _qualityHub = qualityHub;
        _inventoryHub = inventoryHub;
    }

    public async Task<bool> SubmitQCInspectionAsync(QCInspection inspection, string userId)
    {
        await using var transaction = await BeginTransactionIfRelationalAsync();
        try
        {
            var lot = await _context.Lots
                .Include(l => l.Product)
                .FirstOrDefaultAsync(l => l.Id == inspection.LotId);
            if (lot is null)
            {
                return false;
            }

            var balances = await _context.StockBalances
                .Where(sb => sb.LotId == inspection.LotId && sb.QtyOnHold > 0 && sb.Location!.Code != QuarantineLocationCode)
                .AsNoTracking()
                .ToListAsync();
            if (balances.Count == 0)
            {
                return false;
            }

            inspection.InspectionTime = DateTime.UtcNow;
            inspection.InspectorId = userId;
            await EvaluateLinesAsync(inspection, lot.ProductId);

            if (inspection.Lines.Any(l => !l.IsOK))
            {
                inspection.Result = QCResult.REJECT;
            }

            if (!await ClaimHeldStockAsync(inspection, balances))
            {
                return false;
            }

            await _context.QCInspections.AddAsync(inspection);

            if (inspection.Result == QCResult.PASS)
            {
                lot.UnitPrice = await _costingService.CalculateProductionCostAsync(inspection.WorkOrderId);
            }
            else if (inspection.Result == QCResult.REJECT)
            {
                await ConsolidateHoldInQuarantineAsync(balances, userId);
            }

            await _context.SaveChangesAsync();
            await CommitIfRelationalAsync(transaction);

            if (inspection.Result == QCResult.REJECT && _qualityHub is not null)
            {
                await _qualityHub.Clients.All.SendAsync("ReceiveQcAlert", lot.LotNo, inspection.Result.ToString());
            }
            if (_inventoryHub is not null)
            {
                await _inventoryHub.Clients.All.SendAsync("ReceiveStockUpdate");
            }

            return true;
        }
        catch
        {
            await RollbackIfRelationalAsync(transaction);
            throw;
        }
    }

    private async Task EvaluateLinesAsync(QCInspection inspection, int productId)
    {
        var checklist = await _context.QCChecklists
            .Include(c => c.Items)
            .Where(c => c.ProductId == productId && c.IsActive)
            .OrderByDescending(c => c.Id)
            .FirstOrDefaultAsync();

        foreach (var line in inspection.Lines)
        {
            var item = checklist?.Items.FirstOrDefault(i =>
                string.Equals(i.ParameterName, line.ParameterName, StringComparison.OrdinalIgnoreCase));

            if (item is null)
            {
                line.IsOK = IsAffirmative(line.ValueInspected);
                continue;
            }

            if (item.MinVal.HasValue || item.MaxVal.HasValue)
            {
                line.IsOK = decimal.TryParse(line.ValueInspected, out var measuredValue) &&
                    (!item.MinVal.HasValue || measuredValue >= item.MinVal.Value) &&
                    (!item.MaxVal.HasValue || measuredValue <= item.MaxVal.Value);
                continue;
            }

            line.IsOK = IsAffirmative(line.ValueInspected);
        }
    }

    private async Task<bool> ClaimHeldStockAsync(QCInspection inspection, IReadOnlyCollection<StockBalance> balances)
    {
        if (_context.Database.IsRelational())
        {
            var query = _context.StockBalances.Where(sb => sb.LotId == inspection.LotId && sb.QtyOnHold > 0 && sb.Location!.Code != QuarantineLocationCode);
            var affected = inspection.Result == QCResult.PASS
                ? await query.ExecuteUpdateAsync(setters => setters
                    .SetProperty(sb => sb.QtyAvailable, sb => sb.QtyAvailable + sb.QtyOnHold)
                    .SetProperty(sb => sb.QtyOnHold, 0m))
                : await query.ExecuteUpdateAsync(setters => setters.SetProperty(sb => sb.QtyOnHold, 0m));
            return affected == balances.Count;
        }

        var ids = balances.Select(x => x.Id).ToList();
        var tracked = await _context.StockBalances.Where(x => ids.Contains(x.Id) && x.QtyOnHold > 0).ToListAsync();
        if (tracked.Count != balances.Count) return false;
        foreach (var balance in tracked)
        {
            if (inspection.Result == QCResult.PASS) balance.QtyAvailable += balance.QtyOnHold;
            balance.QtyOnHold = 0m;
        }
        return true;
    }

    private async Task ConsolidateHoldInQuarantineAsync(IReadOnlyCollection<StockBalance> sources, string userId)
    {
        var quarantine = await _context.Locations
            .FirstOrDefaultAsync(l => l.Code == QuarantineLocationCode);
        if (quarantine is null)
        {
            throw new InvalidOperationException($"Location {QuarantineLocationCode} was not found.");
        }

        var first = sources.First();
        var target = await _context.StockBalances.FirstOrDefaultAsync(x =>
            x.ProductId == first.ProductId && x.LotId == first.LotId && x.LocationId == quarantine.Id);
        if (target is null)
        {
            target = new StockBalance { ProductId = first.ProductId, LotId = first.LotId, LocationId = quarantine.Id };
            await _context.StockBalances.AddAsync(target);
        }
        target.QtyOnHold += sources.Sum(x => x.QtyOnHold);

        foreach (var source in sources.Where(x => x.LocationId != quarantine.Id))
        {
            var transfer = new StockTransfer
            {
                TransferNo = $"QC-{Guid.NewGuid():N}",
                TransferDate = DateTime.UtcNow,
                Status = DocumentStatus.Completed,
                Lines =
                {
                    new StockTransferLine { ProductId=source.ProductId,LotId=source.LotId,FromLocationId=source.LocationId,ToLocationId=quarantine.Id,Qty=source.QtyOnHold }
                }
            };
            await _context.StockTransfers.AddAsync(transfer);
            await _context.StockTransactions.AddAsync(new StockTransaction { Type=TransactionType.Transfer,ProductId=source.ProductId,LotId=source.LotId,LocationId=quarantine.Id,Qty=0m,TransactionDate=DateTime.UtcNow,UserId=userId,ReferenceNo=transfer.TransferNo });
        }
    }

    private static bool IsAffirmative(string value)
    {
        return value.Equals("PASS", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("TRUE", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("OK", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<IDbContextTransaction?> BeginTransactionIfRelationalAsync()
    {
        return _context.Database.IsRelational()
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

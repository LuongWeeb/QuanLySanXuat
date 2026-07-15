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

    public QcService(ApplicationDbContext context, ICostingService costingService)
        : this(context, costingService, null)
    {
    }

    public QcService(
        ApplicationDbContext context,
        ICostingService costingService,
        IHubContext<QualityHub>? qualityHub)
    {
        _context = context;
        _costingService = costingService;
        _qualityHub = qualityHub;
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

            inspection.InspectionTime = DateTime.UtcNow;
            inspection.InspectorId = userId;
            await EvaluateLinesAsync(inspection, lot.ProductId);

            if (inspection.Lines.Any(l => !l.IsOK))
            {
                inspection.Result = QCResult.REJECT;
            }

            await _context.QCInspections.AddAsync(inspection);
            await _context.SaveChangesAsync();

            var balance = await _context.StockBalances
                .FirstOrDefaultAsync(sb => sb.LotId == inspection.LotId && sb.QtyOnHold > 0);
            if (balance is null)
            {
                return false;
            }

            if (inspection.Result == QCResult.PASS)
            {
                balance.QtyAvailable += balance.QtyOnHold;
                balance.QtyOnHold = 0m;
                lot.UnitPrice = await _costingService.CalculateProductionCostAsync(inspection.WorkOrderId);
            }
            else if (inspection.Result == QCResult.REJECT)
            {
                await MoveHoldToQuarantineAsync(balance, userId);
            }

            await _context.SaveChangesAsync();
            await CommitIfRelationalAsync(transaction);

            if (inspection.Result == QCResult.REJECT && _qualityHub is not null)
            {
                await _qualityHub.Clients.All.SendAsync("ReceiveQcAlert", lot.LotNo, inspection.Result.ToString());
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

    private async Task MoveHoldToQuarantineAsync(StockBalance balance, string userId)
    {
        var quarantine = await _context.Locations
            .FirstOrDefaultAsync(l => l.Code == QuarantineLocationCode);
        if (quarantine is null)
        {
            throw new InvalidOperationException($"Location {QuarantineLocationCode} was not found.");
        }

        var transfer = new StockTransfer
        {
            TransferNo = $"QC-{DateTime.UtcNow:yyyyMMddHHmmssfff}",
            TransferDate = DateTime.UtcNow,
            Status = DocumentStatus.Completed,
            Lines =
            {
                new StockTransferLine
                {
                    ProductId = balance.ProductId,
                    LotId = balance.LotId,
                    FromLocationId = balance.LocationId,
                    ToLocationId = quarantine.Id,
                    Qty = balance.QtyOnHold
                }
            }
        };
        await _context.StockTransfers.AddAsync(transfer);

        await _context.StockTransactions.AddAsync(new StockTransaction
        {
            Type = TransactionType.Transfer,
            ProductId = balance.ProductId,
            LotId = balance.LotId,
            LocationId = quarantine.Id,
            Qty = 0m,
            TransactionDate = DateTime.UtcNow,
            UserId = userId,
            ReferenceNo = transfer.TransferNo
        });

        balance.LocationId = quarantine.Id;
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

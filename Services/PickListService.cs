using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Domain.Enums;

namespace WmsMes.Web.Services;

public class PickListService : IPickListService
{
    private const int MaxNumberAllocationAttempts = 10;
    private const int MaxPickListSequence = 999;
    private readonly ApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;

    public PickListService(ApplicationDbContext context, TimeProvider? timeProvider = null)
    {
        _context = context;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<PickList?> CreatePickListForSalesOrderAsync(int salesOrderId)
    {
        var order = await _context.SalesOrders
            .Where(salesOrder => salesOrder.Id == salesOrderId
                && salesOrder.Status == DocumentStatus.Draft
                && salesOrder.Items.Any(item => item.Qty > item.DeliveredQty))
            .Include(salesOrder => salesOrder.Items)
            .FirstOrDefaultAsync();
        if (order is null)
        {
            return null;
        }

        var allocations = new List<(int ProductId, int LocationId, int LotId, int BalanceId, decimal Quantity, string ZoneCode, string LocationCode)>();
        foreach (var item in order.Items)
        {
            var remainingQuantity = Math.Max(0m, item.Qty - item.DeliveredQty);
            if (remainingQuantity == 0m)
            {
                continue;
            }

            var balances = await _context.StockBalances
                .Include(balance => balance.Location)
                    .ThenInclude(location => location!.Zone)
                .Where(balance => balance.ProductId == item.ProductId && balance.QtyAvailable > 0m)
                .Select(balance => new
                {
                    balance.LocationId,
                    balance.LotId,
                    balance.Id,
                    balance.QtyAvailable,
                    ZoneCode = balance.Location!.Zone!.Code,
                    LocationCode = balance.Location.Code
                })
                .OrderBy(balance => balance.ZoneCode)
                .ThenBy(balance => balance.LocationCode)
                .ThenBy(balance => balance.LocationId)
                .ThenBy(balance => balance.LotId)
                .ThenBy(balance => balance.Id)
                .ToListAsync();

            foreach (var balance in balances)
            {
                if (remainingQuantity == 0m)
                {
                    break;
                }

                var quantityToPick = Math.Min(remainingQuantity, balance.QtyAvailable);
                allocations.Add((
                    item.ProductId,
                    balance.LocationId,
                    balance.LotId,
                    balance.Id,
                    quantityToPick,
                    balance.ZoneCode,
                    balance.LocationCode));
                remainingQuantity -= quantityToPick;
            }
        }

        var orderedAllocations = allocations
            .OrderBy(allocation => allocation.ZoneCode)
            .ThenBy(allocation => allocation.LocationCode)
            .ThenBy(allocation => allocation.LocationId)
            .ThenBy(allocation => allocation.ProductId)
            .ThenBy(allocation => allocation.LotId)
            .ThenBy(allocation => allocation.BalanceId)
            .ToList();
        if (orderedAllocations.Count == 0)
        {
            return null;
        }

        var today = _timeProvider.GetUtcNow().UtcDateTime;
        var prefix = $"PK-{today:yyyyMMdd}-";

        for (var attempt = 0; attempt < MaxNumberAllocationAttempts; attempt++)
        {
            var pickList = new PickList
            {
                PickListNo = await GetNextPickListNumberAsync(prefix),
                SalesOrderId = order.Id,
                CreatedAt = today,
                Status = DocumentStatus.Draft,
                Items = orderedAllocations.Select((allocation, index) => new PickListItem
                {
                    ProductId = allocation.ProductId,
                    LocationId = allocation.LocationId,
                    LotId = allocation.LotId,
                    QtyToPick = allocation.Quantity,
                    SequenceOrder = index + 1
                }).ToList()
            };

            _context.PickLists.Add(pickList);
            try
            {
                await _context.SaveChangesAsync();
                return pickList;
            }
            catch (DbUpdateException exception)
            {
                if (attempt == MaxNumberAllocationAttempts - 1 ||
                    !await IsPickListNumberCollisionAsync(exception, pickList.PickListNo))
                {
                    throw;
                }

                _context.Entry(pickList).State = EntityState.Detached;
                foreach (var item in pickList.Items)
                {
                    _context.Entry(item).State = EntityState.Detached;
                }
            }
        }

        throw new InvalidOperationException("Unable to allocate a unique pick-list number.");
    }

    private async Task<string> GetNextPickListNumberAsync(string prefix)
    {
        var existingNumbers = await _context.PickLists
            .Where(pickList => pickList.PickListNo.StartsWith(prefix))
            .Select(pickList => pickList.PickListNo)
            .ToListAsync();
        var nextSequence = existingNumbers
            .Select(number => int.TryParse(number[prefix.Length..], out var sequence) ? sequence : 0)
            .DefaultIfEmpty()
            .Max() + 1;

        if (nextSequence > MaxPickListSequence)
        {
            throw new InvalidOperationException($"Pick-list numbers are exhausted for {prefix[..^1]}.");
        }

        return $"{prefix}{nextSequence:000}";
    }

    private async Task<bool> IsPickListNumberCollisionAsync(
        DbUpdateException exception,
        string pickListNo)
    {
        if (!IsUniqueConstraintViolation(exception))
        {
            return false;
        }

        return await _context.PickLists
            .AsNoTracking()
            .AnyAsync(candidate => candidate.PickListNo == pickListNo);
    }

    private bool IsUniqueConstraintViolation(DbUpdateException exception)
    {
        if (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return true;
        }

        return exception.InnerException is SqliteException
        {
            SqliteErrorCode: 19,
            SqliteExtendedErrorCode: 2067
        };
    }
}

using Microsoft.EntityFrameworkCore;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Domain.Enums;

namespace WmsMes.Web.Services;

public class PickListService : IPickListService
{
    private const int MaxNumberAllocationAttempts = 10;
    private readonly ApplicationDbContext _context;

    public PickListService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PickList?> CreatePickListForSalesOrderAsync(int salesOrderId)
    {
        var order = await _context.SalesOrders
            .Include(salesOrder => salesOrder.Items)
            .FirstOrDefaultAsync(salesOrder => salesOrder.Id == salesOrderId);
        if (order is null)
        {
            return null;
        }

        var allocations = new List<(int ProductId, int LocationId, int LotId, decimal Quantity, string ZoneCode, string LocationCode)>();
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
                    balance.QtyAvailable,
                    ZoneCode = balance.Location!.Zone!.Code,
                    LocationCode = balance.Location.Code
                })
                .OrderBy(balance => balance.ZoneCode)
                .ThenBy(balance => balance.LocationCode)
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
                    quantityToPick,
                    balance.ZoneCode,
                    balance.LocationCode));
                remainingQuantity -= quantityToPick;
            }
        }

        var orderedAllocations = allocations
            .OrderBy(allocation => allocation.ZoneCode)
            .ThenBy(allocation => allocation.LocationCode)
            .ToList();
        var today = DateTime.UtcNow;
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
            catch (DbUpdateException) when (attempt < MaxNumberAllocationAttempts - 1)
            {
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

        return $"{prefix}{nextSequence:000}";
    }
}

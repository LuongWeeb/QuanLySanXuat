using Microsoft.EntityFrameworkCore;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;

namespace WmsMes.Web.Services;

public static class CycleCountReconciliation
{
    public static async Task PopulateExpectedAtCountQuantitiesAsync(
        ApplicationDbContext context,
        CycleCountOrder order,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(order);

        foreach (var item in order.Items)
        {
            item.ExpectedAtCountQty = item.SystemQty;
        }

        if (!order.CompletedAt.HasValue || order.Items.Count == 0)
        {
            return;
        }

        var productIds = order.Items.Select(item => item.ProductId).Distinct().ToList();
        var lotIds = order.Items.Select(item => item.LotId).Distinct().ToList();
        var locationIds = order.Items.Select(item => item.LocationId).Distinct().ToList();
        var movements = await context.StockTransactions
            .AsNoTracking()
            .Where(transaction =>
                transaction.TransactionDate > order.CreatedAt &&
                transaction.TransactionDate <= order.CompletedAt.Value &&
                productIds.Contains(transaction.ProductId) &&
                lotIds.Contains(transaction.LotId) &&
                locationIds.Contains(transaction.LocationId))
            .Select(transaction => new
            {
                transaction.ProductId,
                transaction.LotId,
                transaction.LocationId,
                transaction.Qty
            })
            .ToListAsync(cancellationToken);

        var netMovements = movements
            .GroupBy(movement => (
                movement.ProductId,
                movement.LotId,
                movement.LocationId))
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Qty));

        foreach (var item in order.Items)
        {
            netMovements.TryGetValue(
                (item.ProductId, item.LotId, item.LocationId),
                out var movement);
            item.ExpectedAtCountQty = item.SystemQty + movement;
        }
    }
}

using Microsoft.EntityFrameworkCore;
using WmsMes.Web.Data;
using WmsMes.Web.ViewModels;

namespace WmsMes.Web.Services;

public static class LowStockQuery
{
    public static IQueryable<LowStockItemViewModel> Create(
        ApplicationDbContext context)
    {
        var totals =
            from product in context.Products.AsNoTracking()
            where product.IsActive && product.MinStock > 0
            join balance in context.StockBalances.AsNoTracking()
                on product.Id equals balance.ProductId into productBalances
            from balance in productBalances.DefaultIfEmpty()
            group balance by new
            {
                product.Id,
                product.Code,
                product.Name,
                product.MinStock,
                product.MaxStock
            }
            into productGroup
            select new LowStockItemViewModel
            {
                ProductId = productGroup.Key.Id,
                ProductCode = productGroup.Key.Code,
                ProductName = productGroup.Key.Name,
                MinStock = productGroup.Key.MinStock,
                MaxStock = productGroup.Key.MaxStock,
                TotalAvailable = productGroup.Sum(balance =>
                    balance == null ? 0m : balance.QtyAvailable)
            };

        return totals
            .Where(item => item.TotalAvailable < item.MinStock)
            .OrderBy(item => item.ProductCode);
    }
}

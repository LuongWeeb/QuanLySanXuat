using WmsMes.Web.Domain.Entities;

namespace WmsMes.Web.ViewModels;

public sealed class StockValuationViewModel
{
    public IReadOnlyList<StockBalance> Balances { get; init; } = Array.Empty<StockBalance>();

    public IReadOnlyList<Warehouse> Warehouses { get; init; } = Array.Empty<Warehouse>();

    public IReadOnlyList<Product> Products { get; init; } = Array.Empty<Product>();

    public int? WarehouseId { get; init; }

    public int? ProductId { get; init; }
}

namespace WmsMes.Web.ViewModels;

public sealed class LowStockItemViewModel
{
    public int ProductId { get; init; }

    public string ProductCode { get; init; } = string.Empty;

    public string ProductName { get; init; } = string.Empty;

    public decimal TotalAvailable { get; init; }

    public decimal MinStock { get; init; }

    public decimal MaxStock { get; init; }

    public decimal SuggestedQty => MaxStock - TotalAvailable;
}

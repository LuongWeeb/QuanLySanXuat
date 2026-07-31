namespace WmsMes.Web.ViewModels;

public sealed class PickListSalesOrderOptionViewModel
{
    public int Id { get; init; }

    public string OrderNo { get; init; } = string.Empty;

    public string CustomerName { get; init; } = string.Empty;

    public decimal RemainingQuantity { get; init; }
}

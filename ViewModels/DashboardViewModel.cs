namespace WmsMes.Web.ViewModels;

public sealed class DashboardViewModel
{
    public int ActiveWorkOrders { get; init; }
    public int PendingQcLots { get; init; }
    public decimal InventoryVolume { get; init; }
}

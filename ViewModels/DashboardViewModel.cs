namespace WmsMes.Web.ViewModels;

public sealed class DashboardViewModel
{
    // Chỉ số vận hành chung
    public int ActiveWorkOrders { get; init; }
    public int PendingQcLots { get; init; }
    public decimal InventoryVolume { get; init; }
    public int LowStockAlertCount { get; init; }

    // Chỉ số OEE (Overall Equipment Effectiveness)
    public decimal OeeAvailabilityPercent { get; init; }
    public decimal OeePerformancePercent { get; init; }
    public decimal OeeQualityPercent { get; init; }
    public decimal OverallOeePercent => Math.Round((OeeAvailabilityPercent * OeePerformancePercent * OeeQualityPercent) / 10000m, 1);

    // Dữ liệu Biểu đồ Sản lượng 7 ngày
    public List<string> DailyLabels { get; init; } = new();
    public List<decimal> DailyPlannedOutput { get; init; } = new();
    public List<decimal> DailyActualOutput { get; init; } = new();

    // Dữ liệu Biểu đồ Phân bổ Tồn kho theo Khu vực (Zone)
    public List<string> ZoneLabels { get; init; } = new();
    public List<decimal> ZoneQuantities { get; init; } = new();

    // Dữ liệu Biểu đồ Chất lượng (Pass / Hold / Quarantine)
    public int PassedQcCount { get; init; }
    public int HoldQcCount { get; init; }
    public int QuarantineQcCount { get; init; }
}

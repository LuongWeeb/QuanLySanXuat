namespace WmsMes.Web.DTOs;

public class OeeMetricsDto
{
    public int WorkCenterId { get; set; }

    public string WorkCenterCode { get; set; } = string.Empty;

    public string WorkCenterName { get; set; } = string.Empty;

    public decimal Availability { get; set; }

    public decimal Performance { get; set; }

    public decimal Quality { get; set; }

    public decimal Oee { get; set; }

    public string StatusColor { get; set; } = "danger";
}

public class InventoryAgingDto
{
    public decimal LessThan30Days { get; set; }

    public decimal Days30To60 { get; set; }

    public decimal Days60To90 { get; set; }

    public decimal MoreThan90Days { get; set; }

    public decimal UnknownAge { get; set; }

    public decimal TotalValue =>
        LessThan30Days +
        Days30To60 +
        Days60To90 +
        MoreThan90Days +
        UnknownAge;
}

public sealed class ProductionQualityAnalyticsDto
{
    public decimal TodayProductionOutput { get; set; }

    public decimal ScrapRate { get; set; }

    public List<ProductionQualityTrendPointDto> DailyTrend { get; set; } = [];
}

public sealed class ProductionQualityTrendPointDto
{
    public string BusinessDate { get; set; } = string.Empty;

    public decimal ScrapQuantity { get; set; }

    public decimal QualityRate { get; set; }
}

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
}

namespace WmsMes.Web.DTOs;

public class ProductionProgressDto
{
    public int WorkOrderId { get; set; }

    public string WorkOrderCode { get; set; } = string.Empty;

    public decimal PlannedQuantity { get; set; }

    public decimal ActualProducedQuantity { get; set; }
}

namespace WmsMes.Web.DTOs;

public class MrpResultDto
{
    public int ComponentProductId { get; set; }

    public string ComponentCode { get; set; } = string.Empty;

    public string ComponentName { get; set; } = string.Empty;

    public decimal GrossDemand { get; set; }

    public decimal StockAvailable { get; set; }

    public decimal NetDemand { get; set; }
}

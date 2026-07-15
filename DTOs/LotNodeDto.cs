namespace WmsMes.Web.DTOs;

public class LotNodeDto
{
    public string LotNo { get; set; } = string.Empty;

    public string ProductCode { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public decimal Qty { get; set; }

    public string ExpiryDate { get; set; } = string.Empty;

    public string Status { get; set; } = "PASS";

    public List<LotNodeDto> Children { get; set; } = new();
}

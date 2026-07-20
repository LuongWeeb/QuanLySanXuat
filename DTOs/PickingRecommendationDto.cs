namespace WmsMes.Web.DTOs;

public enum PickingStrategy
{
    FEFO = 1,
    FIFO = 2
}

public sealed class PickingRecommendationDto
{
    public int ProductId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int LocationId { get; set; }
    public string LocationCode { get; set; } = string.Empty;
    public int LotId { get; set; }
    public string LotNo { get; set; } = string.Empty;
    public DateTime? ExpiryDate { get; set; }
    public DateTime ManufactureDate { get; set; }
    public decimal AvailableQty { get; set; }
    public decimal RecommendedQty { get; set; }
}

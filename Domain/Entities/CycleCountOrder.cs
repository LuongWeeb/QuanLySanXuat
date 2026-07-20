namespace WmsMes.Web.Domain.Entities;

public class CycleCountOrder
{
    public int Id { get; set; }
    public string CountNumber { get; set; } = string.Empty;
    public int WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }
    public string Status { get; set; } = "Draft"; // Draft, InProgress, Completed, Approved, Cancelled
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime? CompletedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public List<CycleCountItem> Items { get; set; } = new();
}

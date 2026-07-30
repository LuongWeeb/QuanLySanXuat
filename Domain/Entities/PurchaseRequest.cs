using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WmsMes.Web.Domain.Enums;

namespace WmsMes.Web.Domain.Entities;

public class PurchaseRequest
{
    public const string OpenLowStockBatchKey = "LOW_STOCK_OPEN";

    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string RequestNo { get; set; } = string.Empty;

    [Required]
    public DateTime RequestDate { get; set; } = DateTime.UtcNow;

    [Required]
    public DateTime RequiredDate { get; set; }

    [Required]
    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;

    public int? ProductionPlanId { get; set; }

    [MaxLength(32)]
    public string? LowStockBatchKey { get; set; }

    [ForeignKey(nameof(ProductionPlanId))]
    public virtual ProductionPlan? ProductionPlan { get; set; }

    public virtual ICollection<PurchaseRequestItem> Items { get; set; } = new List<PurchaseRequestItem>();
}

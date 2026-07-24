using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WmsMes.Web.Domain.Enums;

namespace WmsMes.Web.Domain.Entities;

public class WorkOrder
{
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required]
    public int ProductId { get; set; }

    [ForeignKey(nameof(ProductId))]
    public virtual Product? Product { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Qty { get; set; }

    [Required]
    public DateTime DueDate { get; set; }

    [Required]
    public WorkOrderStatus Status { get; set; } = WorkOrderStatus.Draft;

    [Required]
    [MaxLength(50)]
    public string BomVersion { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string RoutingVersion { get; set; } = string.Empty;

    public virtual ICollection<WorkOrderStep> Steps { get; set; } = new List<WorkOrderStep>();

    public virtual ICollection<DailyProductionLog> DailyProductionLogs { get; set; } = new List<DailyProductionLog>();
}

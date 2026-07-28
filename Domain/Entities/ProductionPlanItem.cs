using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WmsMes.Web.Domain.Entities;

public class ProductionPlanItem
{
    public int Id { get; set; }

    [Required]
    public int ProductionPlanId { get; set; }

    [ForeignKey(nameof(ProductionPlanId))]
    public virtual ProductionPlan? ProductionPlan { get; set; }

    [Required]
    public int ProductId { get; set; }

    [ForeignKey(nameof(ProductId))]
    public virtual Product? Product { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal PlannedQty { get; set; }

    public int? WorkOrderId { get; set; }

    [ForeignKey(nameof(WorkOrderId))]
    public virtual WorkOrder? WorkOrder { get; set; }
}

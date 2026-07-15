using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WmsMes.Web.Domain.Enums;

namespace WmsMes.Web.Domain.Entities;

public class WorkOrderStep
{
    public int Id { get; set; }

    [Required]
    public int WorkOrderId { get; set; }

    [ForeignKey(nameof(WorkOrderId))]
    public virtual WorkOrder? WorkOrder { get; set; }

    [Required]
    public int StepNumber { get; set; }

    [Required]
    [MaxLength(150)]
    public string StepName { get; set; } = string.Empty;

    [Required]
    public int WorkCenterId { get; set; }

    [ForeignKey(nameof(WorkCenterId))]
    public virtual WorkCenter? WorkCenter { get; set; }

    public DateTime? StartTime { get; set; }

    public DateTime? EndTime { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal QtyOK { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal QtyReject { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal QtyRework { get; set; }

    [Required]
    public WorkOrderStepStatus Status { get; set; } = WorkOrderStepStatus.Pending;
}

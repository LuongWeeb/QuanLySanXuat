using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WmsMes.Web.Domain.Entities;

public class DailyProductionLog
{
    public int Id { get; set; }

    [Required]
    public int WorkOrderId { get; set; }

    [ForeignKey(nameof(WorkOrderId))]
    public virtual WorkOrder? WorkOrder { get; set; }

    [Required]
    public DateTime Date { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal QtyProduced { get; set; }

    [MaxLength(250)]
    public string Notes { get; set; } = string.Empty;
}

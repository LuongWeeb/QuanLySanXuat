using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WmsMes.Web.Domain.Entities;

public class RoutingStep
{
    public int Id { get; set; }

    [Required]
    public int RoutingId { get; set; }

    [ForeignKey(nameof(RoutingId))]
    public virtual Routing? Routing { get; set; }

    [Required]
    public int StepNumber { get; set; }

    [Required]
    [MaxLength(150)]
    public string StepName { get; set; } = string.Empty;

    [Required]
    public int WorkCenterId { get; set; }

    [ForeignKey(nameof(WorkCenterId))]
    public virtual WorkCenter? WorkCenter { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal StandardTimeMinutes { get; set; }

    [Required]
    public bool RequireQC { get; set; }
}

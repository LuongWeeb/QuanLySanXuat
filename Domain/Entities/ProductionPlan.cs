using System.ComponentModel.DataAnnotations;
using WmsMes.Web.Domain.Enums;

namespace WmsMes.Web.Domain.Entities;

public class ProductionPlan
{
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string PlanNo { get; set; } = string.Empty;

    [Required]
    public DateTime PlanDate { get; set; } = DateTime.UtcNow;

    [Required]
    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;

    public virtual ICollection<ProductionPlanItem> Items { get; set; } = new List<ProductionPlanItem>();
}

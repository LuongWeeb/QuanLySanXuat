using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WmsMes.Web.Domain.Entities;

public class QCChecklist
{
    public int Id { get; set; }

    [Required]
    public int ProductId { get; set; }

    [ForeignKey(nameof(ProductId))]
    public virtual Product? Product { get; set; }

    public int? StepNumber { get; set; }

    [Required]
    [MaxLength(250)]
    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public virtual ICollection<QCChecklistItem> Items { get; set; } = new List<QCChecklistItem>();
}

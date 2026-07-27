using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WmsMes.Web.Domain.Entities;

public class BOM
{
    public int Id { get; set; }

    [Required]
    public int ProductId { get; set; }

    [ForeignKey(nameof(ProductId))]
    public virtual Product? Product { get; set; }

    [Required]
    [MaxLength(50)]
    public string Version { get; set; } = "V1.0";

    [Required]
    public DateTime EffectiveDate { get; set; } = DateTime.UtcNow;

    [Required]
    public bool IsActive { get; set; } = true;

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalMaterialCost { get; set; } = 0m;

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalOperationCost { get; set; } = 0m;

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalStandardCost { get; set; } = 0m;

    public virtual ICollection<BOMItem> Items { get; set; } = new List<BOMItem>();
}

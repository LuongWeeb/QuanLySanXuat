using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WmsMes.Web.Domain.Entities;

public class BOMItem
{
    public int Id { get; set; }

    [Required]
    public int BomId { get; set; }

    [ForeignKey(nameof(BomId))]
    public virtual BOM? Bom { get; set; }

    [Required]
    public int ComponentProductId { get; set; }

    [ForeignKey(nameof(ComponentProductId))]
    public virtual Product? ComponentProduct { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal QtyPer { get; set; }

    [Column(TypeName = "decimal(5,2)")]
    public decimal ScrapPercent { get; set; }
}

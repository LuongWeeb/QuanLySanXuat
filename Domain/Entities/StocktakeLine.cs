using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WmsMes.Web.Domain.Entities;

public class StocktakeLine
{
    public int Id { get; set; }

    [Required]
    public int StocktakeId { get; set; }

    [ForeignKey(nameof(StocktakeId))]
    public virtual Stocktake? Stocktake { get; set; }

    [Required]
    public int ProductId { get; set; }

    [ForeignKey(nameof(ProductId))]
    public virtual Product? Product { get; set; }

    [Required]
    public int LotId { get; set; }

    [ForeignKey(nameof(LotId))]
    public virtual Lot? Lot { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal QtySystem { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal QtyCounted { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal QtyDiscrepancy { get; set; }
}

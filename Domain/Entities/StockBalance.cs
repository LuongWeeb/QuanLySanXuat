using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WmsMes.Web.Domain.Entities;

public class StockBalance
{
    public int Id { get; set; }

    [Required]
    public int ProductId { get; set; }

    [ForeignKey(nameof(ProductId))]
    public virtual Product? Product { get; set; }

    [Required]
    public int LotId { get; set; }

    [ForeignKey(nameof(LotId))]
    public virtual Lot? Lot { get; set; }

    [Required]
    public int LocationId { get; set; }

    [ForeignKey(nameof(LocationId))]
    public virtual Location? Location { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal QtyAvailable { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal QtyReserved { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal QtyOnHold { get; set; }
}

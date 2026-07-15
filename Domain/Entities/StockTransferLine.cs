using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WmsMes.Web.Domain.Entities;

public class StockTransferLine
{
    public int Id { get; set; }

    [Required]
    public int StockTransferId { get; set; }

    [ForeignKey(nameof(StockTransferId))]
    public virtual StockTransfer? StockTransfer { get; set; }

    [Required]
    public int ProductId { get; set; }

    [ForeignKey(nameof(ProductId))]
    public virtual Product? Product { get; set; }

    [Required]
    public int LotId { get; set; }

    [ForeignKey(nameof(LotId))]
    public virtual Lot? Lot { get; set; }

    [Required]
    public int FromLocationId { get; set; }

    [ForeignKey(nameof(FromLocationId))]
    public virtual Location? FromLocation { get; set; }

    [Required]
    public int ToLocationId { get; set; }

    [ForeignKey(nameof(ToLocationId))]
    public virtual Location? ToLocation { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Qty { get; set; }
}

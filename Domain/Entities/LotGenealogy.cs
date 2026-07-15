using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WmsMes.Web.Domain.Entities;

public class LotGenealogy
{
    public int Id { get; set; }

    [Required]
    public int OutputLotId { get; set; }

    [ForeignKey(nameof(OutputLotId))]
    public virtual Lot? OutputLot { get; set; }

    [Required]
    public int InputLotId { get; set; }

    [ForeignKey(nameof(InputLotId))]
    public virtual Lot? InputLot { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal QtyConsumed { get; set; }
}

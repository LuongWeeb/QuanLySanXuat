using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WmsMes.Web.Domain.Entities;

public class PickListItem
{
    public int Id { get; set; }

    [Required]
    public int PickListId { get; set; }

    [ForeignKey(nameof(PickListId))]
    public virtual PickList? PickList { get; set; }

    [Required]
    public int ProductId { get; set; }

    [ForeignKey(nameof(ProductId))]
    public virtual Product? Product { get; set; }

    [Required]
    public int LocationId { get; set; }

    [ForeignKey(nameof(LocationId))]
    public virtual Location? Location { get; set; }

    [Required]
    public int LotId { get; set; }

    [ForeignKey(nameof(LotId))]
    public virtual Lot? Lot { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal QtyToPick { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal PickedQty { get; set; } = 0m;

    public int SequenceOrder { get; set; } = 1;
}

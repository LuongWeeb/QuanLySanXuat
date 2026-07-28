using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WmsMes.Web.Domain.Entities;

public class PurchaseRequestItem
{
    public int Id { get; set; }

    [Required]
    public int PurchaseRequestId { get; set; }

    [ForeignKey(nameof(PurchaseRequestId))]
    public virtual PurchaseRequest? PurchaseRequest { get; set; }

    [Required]
    public int ProductId { get; set; }

    [ForeignKey(nameof(ProductId))]
    public virtual Product? Product { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Qty { get; set; }
}

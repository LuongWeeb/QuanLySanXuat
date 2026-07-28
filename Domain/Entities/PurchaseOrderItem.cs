using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WmsMes.Web.Domain.Entities;

public class PurchaseOrderItem
{
    public int Id { get; set; }

    [Required]
    public int PurchaseOrderId { get; set; }

    [ForeignKey(nameof(PurchaseOrderId))]
    public virtual PurchaseOrder? PurchaseOrder { get; set; }

    [Required]
    public int ProductId { get; set; }

    [ForeignKey(nameof(ProductId))]
    public virtual Product? Product { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Qty { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal UnitPrice { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal ReceivedQty { get; set; }
}

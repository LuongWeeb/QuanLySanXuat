using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WmsMes.Web.Domain.Enums;

namespace WmsMes.Web.Domain.Entities;

public class GoodsReceipt
{
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string ReceiptNo { get; set; } = string.Empty;

    public int? SupplierId { get; set; }

    [ForeignKey(nameof(SupplierId))]
    public virtual Supplier? Supplier { get; set; }

    public int? PurchaseOrderId { get; set; }

    [ForeignKey(nameof(PurchaseOrderId))]
    public virtual PurchaseOrder? PurchaseOrder { get; set; }

    [Required]
    public DateTime ReceiptDate { get; set; } = DateTime.UtcNow;

    [Required]
    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;

    public virtual ICollection<GoodsReceiptLine> Lines { get; set; } = new List<GoodsReceiptLine>();
}

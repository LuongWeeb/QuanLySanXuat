using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WmsMes.Web.Domain.Enums;

namespace WmsMes.Web.Domain.Entities;

public class PurchaseOrder
{
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string OrderNo { get; set; } = string.Empty;

    [Required]
    public int SupplierId { get; set; }

    [ForeignKey(nameof(SupplierId))]
    public virtual Supplier? Supplier { get; set; }

    [Required]
    public DateTime OrderDate { get; set; } = DateTime.UtcNow;

    [Required]
    public DateTime ExpectedDeliveryDate { get; set; }

    [Required]
    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;

    public int? PurchaseRequestId { get; set; }

    [ForeignKey(nameof(PurchaseRequestId))]
    public virtual PurchaseRequest? PurchaseRequest { get; set; }

    public virtual ICollection<PurchaseOrderItem> Items { get; set; } = new List<PurchaseOrderItem>();
}

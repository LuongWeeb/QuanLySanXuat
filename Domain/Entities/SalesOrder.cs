using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WmsMes.Web.Domain.Enums;

namespace WmsMes.Web.Domain.Entities;

public class SalesOrder
{
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string OrderNo { get; set; } = string.Empty;

    [Required]
    public int CustomerId { get; set; }

    [ForeignKey(nameof(CustomerId))]
    public virtual Customer? Customer { get; set; }

    [Required]
    public DateTime OrderDate { get; set; } = DateTime.UtcNow;

    [Required]
    public DateTime DeliveryDate { get; set; }

    [Required]
    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;

    public virtual ICollection<SalesOrderItem> Items { get; set; } = new List<SalesOrderItem>();
}

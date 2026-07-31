using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WmsMes.Web.Domain.Enums;

namespace WmsMes.Web.Domain.Entities;

public class PickList
{
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string PickListNo { get; set; } = string.Empty;

    [Required]
    public int SalesOrderId { get; set; }

    [ForeignKey(nameof(SalesOrderId))]
    public virtual SalesOrder? SalesOrder { get; set; }

    [Required]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Required]
    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;

    public virtual ICollection<PickListItem> Items { get; set; } = new List<PickListItem>();
}

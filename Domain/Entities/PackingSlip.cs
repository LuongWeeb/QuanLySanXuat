using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WmsMes.Web.Domain.Enums;

namespace WmsMes.Web.Domain.Entities;

public class PackingSlip
{
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string PackingNo { get; set; } = string.Empty;

    [Required]
    public int SalesOrderId { get; set; }

    [ForeignKey(nameof(SalesOrderId))]
    public virtual SalesOrder? SalesOrder { get; set; }

    public int PackageNo { get; set; } = 1;

    [Column(TypeName = "decimal(18,2)")]
    public decimal GrossWeight { get; set; } = 0m;

    [Required]
    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;
}

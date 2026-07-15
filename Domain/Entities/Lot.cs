using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WmsMes.Web.Domain.Entities;

public class Lot
{
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string LotNo { get; set; } = string.Empty;

    [Required]
    public int ProductId { get; set; }

    [ForeignKey(nameof(ProductId))]
    public virtual Product? Product { get; set; }

    public DateTime? ManufactureDate { get; set; }

    public DateTime? ExpiryDate { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Qty { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal UnitPrice { get; set; }

    public int? WorkOrderId { get; set; }

    [ForeignKey(nameof(WorkOrderId))]
    public virtual WorkOrder? WorkOrder { get; set; }
}

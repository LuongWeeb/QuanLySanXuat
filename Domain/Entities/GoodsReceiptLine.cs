using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WmsMes.Web.Domain.Entities;

public class GoodsReceiptLine
{
    public int Id { get; set; }

    [Required]
    public int GoodsReceiptId { get; set; }

    [ForeignKey(nameof(GoodsReceiptId))]
    public virtual GoodsReceipt? GoodsReceipt { get; set; }

    [Required]
    public int ProductId { get; set; }

    [ForeignKey(nameof(ProductId))]
    public virtual Product? Product { get; set; }

    [Required]
    [MaxLength(100)]
    public string LotNo { get; set; } = string.Empty;

    public DateTime? ExpiryDate { get; set; }

    public DateTime? ManufactureDate { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Qty { get; set; }

    [Required]
    public int LocationId { get; set; }

    [ForeignKey(nameof(LocationId))]
    public virtual Location? Location { get; set; }
}

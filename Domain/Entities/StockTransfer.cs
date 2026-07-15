using System.ComponentModel.DataAnnotations;
using WmsMes.Web.Domain.Enums;

namespace WmsMes.Web.Domain.Entities;

public class StockTransfer
{
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string TransferNo { get; set; } = string.Empty;

    [Required]
    public DateTime TransferDate { get; set; } = DateTime.UtcNow;

    [Required]
    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;

    public virtual ICollection<StockTransferLine> Lines { get; set; } = new List<StockTransferLine>();
}

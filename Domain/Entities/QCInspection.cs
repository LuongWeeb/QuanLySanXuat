using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WmsMes.Web.Domain.Enums;

namespace WmsMes.Web.Domain.Entities;

public class QCInspection
{
    public int Id { get; set; }

    public int? WorkOrderId { get; set; }

    [ForeignKey(nameof(WorkOrderId))]
    public virtual WorkOrder? WorkOrder { get; set; }

    public int? GoodsReceiptId { get; set; }

    [ForeignKey(nameof(GoodsReceiptId))]
    public virtual GoodsReceipt? GoodsReceipt { get; set; }

    [Required]
    public QCInspectionType Type { get; set; } = QCInspectionType.FinalFGQC;

    [Required]
    public int LotId { get; set; }

    [ForeignKey(nameof(LotId))]
    public virtual Lot? Lot { get; set; }

    [Required]
    public DateTime InspectionTime { get; set; } = DateTime.UtcNow;

    [Required]
    [MaxLength(450)]
    public string InspectorId { get; set; } = string.Empty;

    [Required]
    public QCResult Result { get; set; } = QCResult.PASS;

    [MaxLength(500)]
    public string Note { get; set; } = string.Empty;

    [MaxLength(500)]
    public string EvidencePath { get; set; } = string.Empty;

    public virtual ICollection<QCInspectionLine> Lines { get; set; } = new List<QCInspectionLine>();
}

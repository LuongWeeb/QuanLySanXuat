using WmsMes.Web.Domain.Enums;

namespace WmsMes.Web.ViewModels;

public class QcPendingLotViewModel
{
    public int LotId { get; set; }
    public string LotNo { get; set; } = string.Empty;
    public string ProductDisplay { get; set; } = string.Empty;
    public QCInspectionType Type { get; set; }
    public decimal QtyOnHold { get; set; }
}

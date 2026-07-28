using System.ComponentModel.DataAnnotations;
using WmsMes.Web.Domain.Enums;

namespace WmsMes.Web.ViewModels;

public class QcInspectionInputModel
{
    [Range(1, int.MaxValue)] public int LotId { get; set; }
    [Range(1, int.MaxValue)] public int ChecklistId { get; set; }
    public QCResult Result { get; set; }
    [MaxLength(500)] public string Note { get; set; } = string.Empty;
    [MaxLength(500)] public string EvidencePath { get; set; } = string.Empty;
    public List<QcMeasurementInputModel> Measurements { get; set; } = [];

    public string LotNo { get; set; } = string.Empty;
    public string ProductDisplay { get; set; } = string.Empty;
    public string ChecklistName { get; set; } = string.Empty;
}

public class QcMeasurementInputModel
{
    public int ChecklistItemId { get; set; }
    public string Value { get; set; } = string.Empty;
    public string ParameterName { get; set; } = string.Empty;
    public decimal? MinVal { get; set; }
    public decimal? MaxVal { get; set; }
    public string Unit { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
}

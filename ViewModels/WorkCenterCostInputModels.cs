using System.ComponentModel.DataAnnotations;

namespace WmsMes.Web.ViewModels;

public static class WorkCenterCostLimits
{
    public const string Decimal18_2Maximum = "9999999999999999.99";
}

public class WorkCenterCreateInputModel
{
    [Required(ErrorMessage = "Mã trạm là bắt buộc.")]
    [StringLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Tên trạm là bắt buộc.")]
    [StringLength(150)]
    public string Name { get; set; } = string.Empty;

    [Range(typeof(decimal), "0", WorkCenterCostLimits.Decimal18_2Maximum,
        ErrorMessage = "Chi phí nhân công không được âm.")]
    public decimal HourlyLaborRate { get; set; }

    [Range(typeof(decimal), "0", WorkCenterCostLimits.Decimal18_2Maximum,
        ErrorMessage = "Chi phí máy móc không được âm.")]
    public decimal HourlyMachineRate { get; set; }
}

public class WorkCenterRateInputModel
{
    [Range(1, int.MaxValue)]
    public int Id { get; set; }

    [Range(typeof(decimal), "0", WorkCenterCostLimits.Decimal18_2Maximum,
        ErrorMessage = "Chi phí nhân công không được âm.")]
    public decimal HourlyLaborRate { get; set; }

    [Range(typeof(decimal), "0", WorkCenterCostLimits.Decimal18_2Maximum,
        ErrorMessage = "Chi phí máy móc không được âm.")]
    public decimal HourlyMachineRate { get; set; }
}

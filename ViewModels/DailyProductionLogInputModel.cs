using System.ComponentModel.DataAnnotations;

namespace WmsMes.Web.ViewModels;

public class DailyProductionLogInputModel
{
    [Required(ErrorMessage = "Ngày sản xuất là bắt buộc.")]
    [DataType(DataType.Date)]
    public DateTime? Date { get; set; }

    [Range(typeof(decimal), "0.01", "79228162514264337593543950335",
        ErrorMessage = "Số lượng sản xuất phải lớn hơn 0.")]
    public decimal QtyProduced { get; set; }

    [StringLength(250, ErrorMessage = "Ghi chú không được vượt quá 250 ký tự.")]
    public string? Notes { get; set; }
}

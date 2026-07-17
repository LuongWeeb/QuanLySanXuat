using System.ComponentModel.DataAnnotations;

namespace WmsMes.Web.ViewModels;

public class WorkOrderCreateInputModel
{
    [Required(ErrorMessage = "Mã lệnh là bắt buộc.")]
    [StringLength(50)]
    public string Code { get; set; } = string.Empty;

    [Range(1, int.MaxValue, ErrorMessage = "Thành phẩm là bắt buộc.")]
    public int ProductId { get; set; }

    [Range(typeof(decimal), "0.01", "79228162514264337593543950335", ErrorMessage = "Số lượng phải lớn hơn 0.")]
    public decimal Qty { get; set; }

    [Required(ErrorMessage = "Hạn hoàn thành là bắt buộc.")]
    public DateTime? DueDate { get; set; }
}

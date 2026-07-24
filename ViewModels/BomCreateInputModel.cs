using System.ComponentModel.DataAnnotations;

namespace WmsMes.Web.ViewModels;

public class BomCreateInputModel
{
    [Range(1, int.MaxValue, ErrorMessage = "Thành phẩm hoặc bán thành phẩm là bắt buộc.")]
    public int ProductId { get; set; }

    [Required(ErrorMessage = "Phiên bản BOM là bắt buộc.")]
    [StringLength(50, ErrorMessage = "Phiên bản BOM không được vượt quá 50 ký tự.")]
    public string Version { get; set; } = string.Empty;

    [Required(ErrorMessage = "Ngày hiệu lực là bắt buộc.")]
    public DateTime? EffectiveDate { get; set; }

    public List<BomItemInputModel> Items { get; set; } = [new()];
}

public class BomItemInputModel
{
    [Range(1, int.MaxValue, ErrorMessage = "Vật tư thành phần là bắt buộc.")]
    public int ComponentProductId { get; set; }

    [Range(typeof(decimal), "0.0001", "79228162514264337593543950335",
        ErrorMessage = "Định mức phải lớn hơn 0.")]
    public decimal QtyPer { get; set; }

    [Range(typeof(decimal), "0", "100", ErrorMessage = "Tỷ lệ hao hụt phải từ 0 đến 100%.")]
    public decimal ScrapPercent { get; set; }
}

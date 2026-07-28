using System.ComponentModel.DataAnnotations;

namespace WmsMes.Web.ViewModels;

public class QcChecklistInputModel
{
    public int Id { get; set; }

    [Range(1, int.MaxValue)]
    public int ProductId { get; set; }

    [Required]
    [MaxLength(250)]
    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public List<QcChecklistItemInputModel> Items { get; set; } = [];
}

public class QcChecklistItemInputModel
{
    [Required]
    [MaxLength(150)]
    public string ParameterName { get; set; } = string.Empty;

    public decimal? MinVal { get; set; }

    public decimal? MaxVal { get; set; }

    [MaxLength(50)]
    public string Unit { get; set; } = string.Empty;

    public bool IsRequired { get; set; } = true;
}

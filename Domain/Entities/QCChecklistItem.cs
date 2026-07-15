using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WmsMes.Web.Domain.Entities;

public class QCChecklistItem
{
    public int Id { get; set; }

    [Required]
    public int QCChecklistId { get; set; }

    [ForeignKey(nameof(QCChecklistId))]
    public virtual QCChecklist? QCChecklist { get; set; }

    [Required]
    [MaxLength(150)]
    public string ParameterName { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,4)")]
    public decimal? MinVal { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal? MaxVal { get; set; }

    [MaxLength(50)]
    public string Unit { get; set; } = string.Empty;

    public bool IsRequired { get; set; } = true;
}

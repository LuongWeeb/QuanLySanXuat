using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WmsMes.Web.Domain.Entities;

public class QCInspectionLine
{
    public int Id { get; set; }

    [Required]
    public int QCInspectionId { get; set; }

    [ForeignKey(nameof(QCInspectionId))]
    public virtual QCInspection? QCInspection { get; set; }

    [Required]
    [MaxLength(150)]
    public string ParameterName { get; set; } = string.Empty;

    [Required]
    [MaxLength(250)]
    public string ValueInspected { get; set; } = string.Empty;

    public bool IsOK { get; set; }
}

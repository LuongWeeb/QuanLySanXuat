using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WmsMes.Web.Domain.Entities;

public class WorkCenter
{
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")]
    public decimal HourlyLaborRate { get; set; } = 0m;

    [Column(TypeName = "decimal(18,2)")]
    public decimal HourlyMachineRate { get; set; } = 0m;

    public bool IsActive { get; set; } = true;
}

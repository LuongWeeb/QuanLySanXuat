using System.ComponentModel.DataAnnotations;

namespace WmsMes.Web.Domain.Entities;

public class Warehouse
{
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public virtual ICollection<Zone> Zones { get; set; } = new List<Zone>();
}

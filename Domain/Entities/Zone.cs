using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WmsMes.Web.Domain.Entities;

public class Zone
{
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public int WarehouseId { get; set; }

    [ForeignKey(nameof(WarehouseId))]
    public virtual Warehouse? Warehouse { get; set; }

    public bool IsActive { get; set; } = true;

    public virtual ICollection<Location> Locations { get; set; } = new List<Location>();
}

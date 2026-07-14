using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WmsMes.Web.Domain.Entities;

public class Location
{
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public int ZoneId { get; set; }

    [ForeignKey(nameof(ZoneId))]
    public virtual Zone? Zone { get; set; }

    public bool IsActive { get; set; } = true;
}

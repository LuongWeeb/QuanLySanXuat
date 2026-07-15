using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WmsMes.Web.Domain.Entities;

public class Routing
{
    public int Id { get; set; }

    [Required]
    public int ProductId { get; set; }

    [ForeignKey(nameof(ProductId))]
    public virtual Product? Product { get; set; }

    [Required]
    [MaxLength(250)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string Version { get; set; } = "V1.0";

    [Required]
    public bool IsActive { get; set; } = true;

    public virtual ICollection<RoutingStep> Steps { get; set; } = new List<RoutingStep>();
}

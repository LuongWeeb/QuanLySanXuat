using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WmsMes.Web.Domain.Enums;

namespace WmsMes.Web.Domain.Entities;

public class Stocktake
{
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string StocktakeNo { get; set; } = string.Empty;

    [Required]
    public int LocationId { get; set; }

    [ForeignKey(nameof(LocationId))]
    public virtual Location? Location { get; set; }

    [Required]
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    [Required]
    public StocktakeStatus Status { get; set; } = StocktakeStatus.Draft;

    public virtual ICollection<StocktakeLine> Lines { get; set; } = new List<StocktakeLine>();
}

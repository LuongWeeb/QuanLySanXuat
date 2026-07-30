using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WmsMes.Web.Domain.Entities;

public class CycleCountItem
{
    public int Id { get; set; }
    public int CycleCountOrderId { get; set; }
    public CycleCountOrder? CycleCountOrder { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public int LocationId { get; set; }
    public Location? Location { get; set; }
    public int LotId { get; set; }
    public Lot? Lot { get; set; }
    public decimal SystemQty { get; set; }
    public decimal? CountedQty { get; set; }
    public decimal VarianceQty => (CountedQty ?? SystemQty) - SystemQty;

    [NotMapped]
    public decimal? ExpectedAtCountQty { get; set; }

    [NotMapped]
    public decimal AuthoritativeVarianceQty
    {
        get
        {
            var expected = ExpectedAtCountQty ?? SystemQty;
            return (CountedQty ?? expected) - expected;
        }
    }

    [MaxLength(250)]
    public string? ReasonNote { get; set; }
}

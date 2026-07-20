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
}

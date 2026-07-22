using WmsMes.Web.Domain.Entities;

namespace WmsMes.Web.ViewModels;

public sealed class SearchResultViewModel
{
    public string Query { get; set; } = string.Empty;
    public List<Product> Products { get; set; } = new();
    public List<WorkOrder> WorkOrders { get; set; } = new();
    public List<Lot> Lots { get; set; } = new();
    public List<Location> Locations { get; set; } = new();
}

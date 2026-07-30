using WmsMes.Web.ViewModels;

namespace WmsMes.Web.Services;

public interface ILowStockService
{
    Task<IReadOnlyList<LowStockItemViewModel>> GetLowStockItemsAsync(
        CancellationToken cancellationToken = default);
}

using WmsMes.Web.Domain.Entities;

namespace WmsMes.Web.Services;

public interface IInventoryService
{
    Task<IEnumerable<StockBalance>> GetSuggestedLotsAsync(int productId, decimal qty);

    Task<bool> CompleteGoodsReceiptAsync(int receiptId, string userId);

    Task<bool> CompleteGoodsReceiptWithoutNotificationAsync(int receiptId, string userId);

    Task<bool> CompleteGoodsIssueAsync(int issueId, string userId);

    Task<bool> CompleteGoodsIssueWithoutNotificationAsync(int issueId, string userId);

    Task NotifyStockChangedAsync();

    Task<bool> StartStocktakeAsync(int stocktakeId);

    Task<bool> ApproveStocktakeAsync(int stocktakeId, string userId);
}

using WmsMes.Web.Domain.Entities;
using WmsMes.Web.DTOs;

namespace WmsMes.Web.Services;

public interface IInventoryService
{
    Task<List<PickingRecommendationDto>> GetPickingRecommendationsAsync(int productId, decimal requiredQty, PickingStrategy strategy);

    Task<IEnumerable<StockBalance>> GetSuggestedLotsAsync(int productId, decimal qty);

    Task<bool> CompleteGoodsReceiptAsync(int receiptId, string userId);

    Task<bool> CompleteGoodsReceiptWithoutNotificationAsync(int receiptId, string userId);

    Task<bool> CompleteGoodsIssueAsync(int issueId, string userId);

    Task<bool> CompleteGoodsIssueWithoutNotificationAsync(int issueId, string userId);

    Task NotifyStockChangedAsync();

    Task<bool> StartStocktakeAsync(int stocktakeId);

    Task<bool> ApproveStocktakeAsync(int stocktakeId, string userId);

    Task<bool> AdjustStockAsync(int productId, int lotId, int locationId, decimal adjustmentQty, string userId, string referenceNo);
}

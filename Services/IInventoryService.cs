using WmsMes.Web.Domain.Entities;

namespace WmsMes.Web.Services;

public interface IInventoryService
{
    Task<IEnumerable<StockBalance>> GetSuggestedLotsAsync(int productId, decimal qty);

    Task<bool> CompleteGoodsReceiptAsync(int receiptId, string userId);

    Task<bool> CompleteGoodsIssueAsync(int issueId, string userId);
}

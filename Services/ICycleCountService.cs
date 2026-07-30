using WmsMes.Web.Domain.Entities;
using WmsMes.Web.DTOs;

namespace WmsMes.Web.Services;

public interface ICycleCountService
{
    Task<CycleCountOrder?> GetByIdAsync(int id);

    Task<CycleCountOrder> CreateOrderAsync(int warehouseId, string createdBy);

    Task<bool> UpdateCountedQtysAsync(
        int orderId,
        Dictionary<int, decimal> itemCounts);

    Task<bool> UpdateCountedQtysAsync(
        int orderId,
        Dictionary<int, decimal> itemCounts,
        Dictionary<int, string?> itemReasons);

    Task<bool> AddDiscoveredItemAsync(
        int orderId,
        string locationCode,
        string lotNo,
        decimal countedQty);

    Task<bool> ApproveAndAdjustLedgerAsync(
        int orderId,
        string managerUserId);

    Task<CycleCountOrder> CreateCycleCountOrderAsync(int warehouseId, string createdBy);

    Task<bool> RecordCountResultsAsync(int orderId, List<CountResultDto> results);

    Task<bool> ApproveAndAdjustStockAsync(int orderId, string approvedBy);
}

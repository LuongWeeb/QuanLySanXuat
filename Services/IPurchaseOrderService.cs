using WmsMes.Web.Domain.Entities;

namespace WmsMes.Web.Services;

public interface IPurchaseOrderService
{
    Task<IReadOnlyList<PurchaseOrder>> GetAllAsync();

    Task<PurchaseOrder?> GetByIdAsync(int id);

    Task<PurchaseOrder?> CreateOrderFromRequestAsync(
        int requestId,
        int supplierId,
        string userId);
}

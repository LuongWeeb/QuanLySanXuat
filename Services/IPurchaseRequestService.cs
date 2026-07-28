using WmsMes.Web.Domain.Entities;

namespace WmsMes.Web.Services;

public interface IPurchaseRequestService
{
    Task<IReadOnlyList<PurchaseRequest>> GetAllAsync();

    Task<PurchaseRequest?> GetByIdAsync(int id);

    Task<PurchaseRequest?> GenerateFromMrpAsync(int productionPlanId, string userId);
}

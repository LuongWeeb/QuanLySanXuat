using WmsMes.Web.Domain.Entities;
using WmsMes.Web.DTOs;

namespace WmsMes.Web.Services;

public interface IProductionPlanService
{
    Task<ProductionPlan?> GetByIdAsync(int id);

    Task<bool> CreatePlanAsync(ProductionPlan plan);

    Task<IEnumerable<MrpResultDto>> CalculatePlanRequirementsAsync(int planId);

    Task<bool> GenerateWorkOrdersAsync(int planId, string userId);

    Task<bool> CompletePlanAsync(int planId);
}

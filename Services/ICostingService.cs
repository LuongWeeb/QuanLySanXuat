namespace WmsMes.Web.Services;

public interface ICostingService
{
    Task<decimal> CalculateProductionCostAsync(int workOrderId);
}

using WmsMes.Web.DTOs;

namespace WmsMes.Web.Services;

public interface IOeeService
{
    Task<OeeMetricsDto> GetWorkCenterOeeAsync(int workCenterId, DateTime startDate, DateTime endDate);

    Task<IEnumerable<OeeMetricsDto>> GetAllWorkCentersOeeAsync(DateTime startDate, DateTime endDate);

    Task<InventoryAgingDto> GetInventoryAgingAnalyticsAsync();
}

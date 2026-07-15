using WmsMes.Web.DTOs;

namespace WmsMes.Web.Services;

public interface ITraceabilityService
{
    Task<LotNodeDto?> GetBackwardTraceAsync(string lotNo);

    Task<LotNodeDto?> GetForwardTraceAsync(string lotNo);
}

using WmsMes.Web.DTOs;

namespace WmsMes.Web.Services;

public interface IMrpService
{
    Task<IEnumerable<MrpResultDto>> CalculateRequirementsAsync(int productId, decimal qty);
}

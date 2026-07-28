using WmsMes.Web.Domain.Entities;

namespace WmsMes.Web.Services;

public interface ISalesOrderService
{
    Task<IReadOnlyList<SalesOrder>> GetAllAsync();

    Task<SalesOrder?> GetByIdAsync(int id);

    Task<SalesOrder?> CreateAsync(SalesOrder order, string userId);
}

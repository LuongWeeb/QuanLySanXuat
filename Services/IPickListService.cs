using WmsMes.Web.Domain.Entities;

namespace WmsMes.Web.Services;

public interface IPickListService
{
    Task<PickList?> CreatePickListForSalesOrderAsync(int salesOrderId);
}

using WmsMes.Web.Domain.Entities;

namespace WmsMes.Web.Services;

public interface IWorkOrderService
{
    Task<WorkOrder?> GetByIdAsync(int id);

    Task<bool> CreateWorkOrderAsync(WorkOrder workOrder);

    Task<bool> ApproveWorkOrderAsync(int workOrderId, string userId);

    Task<bool> StartStepAsync(int stepId);

    Task<bool> CompleteStepAsync(int stepId, decimal qtyOk, decimal qtyReject, decimal qtyRework);

    Task<bool> CompleteWorkOrderAsync(int workOrderId, string userId);
}

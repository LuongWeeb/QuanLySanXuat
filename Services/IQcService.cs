using WmsMes.Web.Domain.Entities;

namespace WmsMes.Web.Services;

public interface IQcService
{
    Task<bool> SubmitQCInspectionAsync(QCInspection inspection, string userId);
}

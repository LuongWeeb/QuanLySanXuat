namespace WmsMes.Web.Services;

public interface IReportExportService
{
    Task<byte[]> ExportStockBalanceToExcelAsync(int? warehouseId = null);
    Task<byte[]> ExportWorkOrderToPdfAsync(int workOrderId);
}

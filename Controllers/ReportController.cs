using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;

namespace WmsMes.Web.Controllers;

[Authorize]
public class ReportController : Controller
{
    private const string WorksheetName = "Báo cáo Tài chính Kho";
    private readonly ApplicationDbContext _context;

    public ReportController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> StockValuation()
    {
        return View(await GetStockValuationBalancesAsync());
    }

    [HttpGet]
    public async Task<IActionResult> ExportStockValuationExcel()
    {
        var balances = await GetStockValuationBalancesAsync();
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add(WorksheetName);

        worksheet.Range("A1:H1").Merge();
        worksheet.Cell(1, 1).Value = "BÁO CÁO GIÁ TRỊ TỒN KHO & TÀI CHÍNH";
        worksheet.Cell(1, 1).Style.Font.Bold = true;
        worksheet.Cell(1, 1).Style.Font.FontSize = 16;
        worksheet.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        var headers = new[]
        {
            "Mã SKU", "Tên sản phẩm", "Tên kho", "Vị trí", "Số lô",
            "Số lượng tồn", "Đơn giá vốn (VNĐ)", "Tổng giá trị (VNĐ)"
        };
        for (var column = 0; column < headers.Length; column++)
        {
            worksheet.Cell(3, column + 1).Value = headers[column];
        }

        var headerRange = worksheet.Range("A3:H3");
        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#0D6EFD");
        headerRange.Style.Font.FontColor = XLColor.White;
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        var row = 4;
        var totalValue = 0m;
        foreach (var balance in balances)
        {
            var unitPrice = balance.Lot?.UnitPrice ?? 0m;
            var lineValue = balance.QtyAvailable * unitPrice;
            worksheet.Cell(row, 1).Value = balance.Product?.Code ?? string.Empty;
            worksheet.Cell(row, 2).Value = balance.Product?.Name ?? string.Empty;
            worksheet.Cell(row, 3).Value = balance.Location?.Zone?.Warehouse?.Name ?? string.Empty;
            worksheet.Cell(row, 4).Value = balance.Location?.Code ?? string.Empty;
            worksheet.Cell(row, 5).Value = balance.Lot?.LotNo ?? string.Empty;
            worksheet.Cell(row, 6).Value = balance.QtyAvailable;
            worksheet.Cell(row, 7).Value = unitPrice;
            worksheet.Cell(row, 8).Value = lineValue;
            totalValue += lineValue;
            row++;
        }

        worksheet.Range(4, 6, row - 1, 8).Style.NumberFormat.Format = "#,##0.00";
        worksheet.Cell(row, 7).Value = "TỔNG CỘNG";
        worksheet.Cell(row, 8).Value = totalValue;
        worksheet.Range(row, 7, row, 8).Style.Font.Bold = true;
        worksheet.Cell(row, 8).Style.NumberFormat.Format = "#,##0.00";
        worksheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return File(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"BaoCao_TaiChinh_Kho_{DateTime.UtcNow:yyyyMMdd}.xlsx");
    }

    private Task<List<StockBalance>> GetStockValuationBalancesAsync()
    {
        return _context.StockBalances
            .AsNoTracking()
            .Include(balance => balance.Product)
            .Include(balance => balance.Lot)
            .Include(balance => balance.Location)
                .ThenInclude(location => location!.Zone)
                    .ThenInclude(zone => zone!.Warehouse)
            .Where(balance => balance.QtyAvailable > 0)
            .OrderBy(balance => balance.Product!.Code)
            .ThenBy(balance => balance.Location!.Zone!.Warehouse!.Code)
            .ThenBy(balance => balance.Location!.Code)
            .ThenBy(balance => balance.Lot!.LotNo)
            .ThenBy(balance => balance.Id)
            .ToListAsync();
    }
}

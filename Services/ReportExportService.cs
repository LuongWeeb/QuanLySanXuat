using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using WmsMes.Web.Data;

namespace WmsMes.Web.Services;

public class ReportExportService : IReportExportService
{
    private const string HeaderColor = "#1E293B";
    private readonly ApplicationDbContext _context;

    public ReportExportService(ApplicationDbContext context)
    {
        _context = context;
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public async Task<byte[]> ExportStockBalanceToExcelAsync(int? warehouseId = null)
    {
        var query = _context.StockBalances
            .AsNoTracking()
            .Include(balance => balance.Product)
                .ThenInclude(product => product!.BaseUom)
            .Include(balance => balance.Lot)
            .Include(balance => balance.Location)
                .ThenInclude(location => location!.Zone)
            .AsQueryable();

        if (warehouseId.HasValue)
        {
            query = query.Where(balance =>
                balance.Location!.Zone!.WarehouseId == warehouseId.Value);
        }

        var balances = await query
            .OrderBy(balance => balance.Product!.Code)
            .ThenBy(balance => balance.Lot!.LotNo)
            .ThenBy(balance => balance.Location!.Code)
            .ToListAsync();

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Tồn kho");
        var headers = new[]
        {
            "Mã SP",
            "Tên SP",
            "Lô",
            "Vị trí",
            "Số lượng khả dụng",
            "Đơn vị tính",
            "Hạn dùng"
        };

        for (var column = 0; column < headers.Length; column++)
        {
            worksheet.Cell(1, column + 1).Value = headers[column];
        }

        var header = worksheet.Range(1, 1, 1, headers.Length);
        header.Style.Font.Bold = true;
        header.Style.Font.FontColor = XLColor.White;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml(HeaderColor);
        header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        for (var index = 0; index < balances.Count; index++)
        {
            var row = index + 2;
            var balance = balances[index];
            worksheet.Cell(row, 1).Value = balance.Product?.Code ?? string.Empty;
            worksheet.Cell(row, 2).Value = balance.Product?.Name ?? string.Empty;
            worksheet.Cell(row, 3).Value = balance.Lot?.LotNo ?? string.Empty;
            worksheet.Cell(row, 4).Value = balance.Location?.Code ?? string.Empty;
            worksheet.Cell(row, 5).Value = balance.QtyAvailable;
            worksheet.Cell(row, 6).Value = balance.Product?.BaseUom?.Code ?? string.Empty;

            if (balance.Lot?.ExpiryDate is DateTime expiryDate)
            {
                worksheet.Cell(row, 7).Value = expiryDate;
                worksheet.Cell(row, 7).Style.DateFormat.Format = "dd/MM/yyyy";
            }
        }

        worksheet.Column(5).Style.NumberFormat.Format = "#,##0.00";
        worksheet.RangeUsed()?.SetAutoFilter();
        worksheet.SheetView.FreezeRows(1);
        worksheet.Columns(1, headers.Length).AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public async Task<byte[]> ExportWorkOrderToPdfAsync(int workOrderId)
    {
        var workOrder = await _context.WorkOrders
            .AsNoTracking()
            .Include(order => order.Product)
            .Include(order => order.Steps)
                .ThenInclude(step => step.WorkCenter)
            .SingleOrDefaultAsync(order => order.Id == workOrderId)
            ?? throw new KeyNotFoundException($"Work order {workOrderId} was not found.");

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(style => style.FontSize(10));

                page.Header()
                    .BorderBottom(2)
                    .BorderColor(HeaderColor)
                    .PaddingBottom(10)
                    .Text("PHIẾU LỆNH SẢN XUẤT")
                    .SemiBold()
                    .FontSize(20)
                    .FontColor(HeaderColor);

                page.Content().PaddingVertical(18).Column(column =>
                {
                    column.Spacing(14);
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(115);
                            columns.RelativeColumn();
                            columns.ConstantColumn(115);
                            columns.RelativeColumn();
                        });

                        AddLabelValue(table, "Mã lệnh", workOrder.Code);
                        AddLabelValue(table, "Sản phẩm", $"{workOrder.Product?.Code} - {workOrder.Product?.Name}");
                        AddLabelValue(table, "Số lượng mục tiêu", workOrder.Qty.ToString("#,##0.00"));
                        AddLabelValue(table, "Hạn hoàn thành", workOrder.DueDate.ToString("dd/MM/yyyy"));
                    });

                    column.Item().Text("CÁC CÔNG ĐOẠN SẢN XUẤT").SemiBold().FontColor(HeaderColor);
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(40);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                        });

                        table.Header(header =>
                        {
                            header.Cell().Element(TableHeaderCell).Text("STT");
                            header.Cell().Element(TableHeaderCell).Text("Công đoạn");
                            header.Cell().Element(TableHeaderCell).Text("Trung tâm");
                            header.Cell().Element(TableHeaderCell).Text("Trạng thái");
                        });

                        foreach (var step in workOrder.Steps.OrderBy(step => step.StepNumber))
                        {
                            table.Cell().Element(TableBodyCell).AlignCenter().Text(step.StepNumber.ToString());
                            table.Cell().Element(TableBodyCell).Text(step.StepName);
                            table.Cell().Element(TableBodyCell).Text(step.WorkCenter?.Code ?? string.Empty);
                            table.Cell().Element(TableBodyCell).Text(step.Status.ToString());
                        }
                    });

                    column.Item().Text("MÃ VẠCH LỆNH SẢN XUẤT").SemiBold().FontColor(HeaderColor);
                    column.Item().Element(container => ComposeCode39Barcode(container, workOrder.Code));
                    column.Item().AlignCenter().Text(workOrder.Code).LetterSpacing(1.5f);
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("Trang ");
                    text.CurrentPageNumber();
                    text.Span(" / ");
                    text.TotalPages();
                });
            });
        });

        return document.GeneratePdf();
    }

    private static void AddLabelValue(TableDescriptor table, string label, string value)
    {
        table.Cell().Element(DetailLabelCell).Text(label);
        table.Cell().Element(DetailValueCell).Text(value);
    }

    private static IContainer DetailLabelCell(IContainer container) =>
        container.Background(Colors.Grey.Lighten3).Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(6);

    private static IContainer DetailValueCell(IContainer container) =>
        container.Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(6);

    private static IContainer TableHeaderCell(IContainer container) =>
        container.Background(HeaderColor).Padding(6).DefaultTextStyle(style => style.FontColor(Colors.White).SemiBold());

    private static IContainer TableBodyCell(IContainer container) =>
        container.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(6);

    private static void ComposeCode39Barcode(IContainer container, string code)
    {
        var encoded = $"*{new string(code.ToUpperInvariant().Select(character =>
            Code39Patterns.ContainsKey(character) ? character : '-').ToArray())}*";

        container.Height(64).Row(row =>
        {
            foreach (var character in encoded)
            {
                var pattern = Code39Patterns[character];
                for (var index = 0; index < pattern.Length; index++)
                {
                    var width = pattern[index] == 'w' ? 3 : 1;
                    row.RelativeItem(width).Background(index % 2 == 0 ? Colors.Black : Colors.White);
                }

                row.RelativeItem(1).Background(Colors.White);
            }
        });
    }

    private static readonly IReadOnlyDictionary<char, string> Code39Patterns = new Dictionary<char, string>
    {
        ['0'] = "nnwwnwnnn", ['1'] = "wnnwnnnnw", ['2'] = "nnwwnnnnw", ['3'] = "wnwwnnnnn",
        ['4'] = "nnnwwnnnw", ['5'] = "wnnwwnnnn", ['6'] = "nnwwwnnnn", ['7'] = "nnnwnnwnw",
        ['8'] = "wnnwnnwnn", ['9'] = "nnwwnnwnn", ['A'] = "wnnnnwnnw", ['B'] = "nnwnnwnnw",
        ['C'] = "wnwnnwnnn", ['D'] = "nnnnwwnnw", ['E'] = "wnnnwwnnn", ['F'] = "nnwnwwnnn",
        ['G'] = "nnnnnwwnw", ['H'] = "wnnnnwwnn", ['I'] = "nnwnnwwnn", ['J'] = "nnnnwwwnn",
        ['K'] = "wnnnnnnww", ['L'] = "nnwnnnnww", ['M'] = "wnwnnnnwn", ['N'] = "nnnnwnnww",
        ['O'] = "wnnnwnnwn", ['P'] = "nnwnwnnwn", ['Q'] = "nnnnnnwww", ['R'] = "wnnnnnwwn",
        ['S'] = "nnwnnnwwn", ['T'] = "nnnnwnwwn", ['U'] = "wwnnnnnnw", ['V'] = "nwwnnnnnw",
        ['W'] = "wwwnnnnnn", ['X'] = "nwnnwnnnw", ['Y'] = "wwnnwnnnn", ['Z'] = "nwwnwnnnn",
        ['-'] = "nwnnnnwnw", ['.'] = "wwnnnnwnn", [' '] = "nwwnnnwnn", ['$'] = "nwnwnwnnn",
        ['/'] = "nwnwnnnwn", ['+'] = "nwnnnwnwn", ['%'] = "nnnwnwnwn", ['*'] = "nwnnwnwnn"
    };
}

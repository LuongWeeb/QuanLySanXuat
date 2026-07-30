using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Common;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Services;

namespace WmsMes.Web.Controllers;

[Authorize]
[Route("api/[controller]")]
[ApiController]
public class PrintController : ControllerBase
{
    private const string CycleCountTitle = "BIÊN BẢN KIỂM KÊ VÀ ĐỐI CHIẾU TỒN KHO";
    private const string ReceiptTitle = "PHIẾU NHẬP KHO";
    private const string IssueTitle = "PHIẾU XUẤT KHO";
    private const string VarianceReasonHeader = "Lý do chênh lệch";
    private const string CounterSignatureTitle = "Người kiểm đếm (Thủ kho)";
    private const string AuditorSignatureTitle = "Nhân viên Kiểm toán/QC";
    private const string ApproverSignatureTitle = "Trưởng kho/Giám đốc duyệt";
    private readonly ApplicationDbContext _context;
    private readonly TimeZoneInfo _businessTimeZone;

    public PrintController(
        ApplicationDbContext context,
        TimeZoneInfo? businessTimeZone = null)
    {
        _context = context;
        _businessTimeZone = businessTimeZone ??
            BusinessTimeZoneResolver.Resolve("Asia/Ho_Chi_Minh");
    }

    [HttpGet("location/{id:int}")]
    public async Task<IActionResult> PrintLocation(int id)
    {
        var location = await _context.Locations
            .Include(item => item.Zone)
            .ThenInclude(zone => zone!.Warehouse)
            .FirstOrDefaultAsync(item => item.Id == id);

        if (location is null)
        {
            return NotFound("Vị trí không tồn tại.");
        }

        var qrCodeBytes = GenerateQrCode(location.Code);
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(new PageSize(100, 50, Unit.Millimetre));
                page.Margin(4, Unit.Millimetre);
                page.Content().Row(row =>
                {
                    row.ConstantItem(38, Unit.Millimetre)
                        .AlignCenter()
                        .AlignMiddle()
                        .Image(qrCodeBytes)
                        .FitArea();
                    row.RelativeItem()
                        .PaddingLeft(3, Unit.Millimetre)
                        .AlignMiddle()
                        .Column(column =>
                        {
                            column.Item().Text(Abbreviate(location.Zone?.Warehouse?.Name ?? "WMS WAREHOUSE", 36))
                                .FontSize(8)
                                .Bold();
                            column.Item().Text($"Khu vực: {Abbreviate(location.Zone?.Code ?? "N/A", 24)}")
                                .FontSize(8);
                            column.Item().PaddingTop(2, Unit.Millimetre).Text(Abbreviate(location.Code, 18))
                                .FontSize(16)
                                .Bold()
                                .FontColor(Colors.Blue.Darken4);
                        });
                });
            });
        });

        return File(document.GeneratePdf(), "application/pdf");
    }

    [HttpGet("lot/{id:int}")]
    public async Task<IActionResult> PrintLot(int id)
    {
        var lot = await _context.Lots
            .Include(item => item.Product)
            .FirstOrDefaultAsync(item => item.Id == id);

        if (lot is null)
        {
            return NotFound("Lô hàng không tồn tại.");
        }

        var qrCodeBytes = GenerateQrCode(lot.LotNo);
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(new PageSize(100, 50, Unit.Millimetre));
                page.Margin(4, Unit.Millimetre);
                page.Content().Row(row =>
                {
                    row.ConstantItem(38, Unit.Millimetre)
                        .AlignCenter()
                        .AlignMiddle()
                        .Image(qrCodeBytes)
                        .FitArea();
                    row.RelativeItem()
                        .PaddingLeft(3, Unit.Millimetre)
                        .AlignMiddle()
                        .Column(column =>
                        {
                            column.Item().Text(Abbreviate(lot.Product?.Name ?? "SẢN PHẨM", 36))
                                .FontSize(8)
                                .Bold();
                            column.Item().Text($"SKU: {Abbreviate(lot.Product?.Code ?? "N/A", 25)}").FontSize(8);
                            column.Item().Text($"NSX: {lot.ManufactureDate?.ToString("dd/MM/yyyy") ?? "N/A"}")
                                .FontSize(7);
                            column.Item().Text($"HSD: {lot.ExpiryDate?.ToString("dd/MM/yyyy") ?? "N/A"}")
                                .FontSize(7);
                            column.Item().PaddingTop(1, Unit.Millimetre).Text(Abbreviate(lot.LotNo, 18))
                                .FontSize(14)
                                .Bold()
                                .FontColor(Colors.Green.Darken4);
                        });
                });
            });
        });

        return File(document.GeneratePdf(), "application/pdf");
    }

    [HttpGet("cyclecount/{id:int}")]
    [Authorize(Roles = "Admin,Warehouse,Manager")]
    public async Task<IActionResult> PrintCycleCount(int id)
    {
        var count = await _context.CycleCountOrders
            .AsNoTracking()
            .Include(order => order.Warehouse)
            .Include(order => order.Items)
                .ThenInclude(item => item.Product)
            .Include(order => order.Items)
                .ThenInclude(item => item.Location)
            .Include(order => order.Items)
                .ThenInclude(item => item.Lot)
            .SingleOrDefaultAsync(order => order.Id == id);

        if (count is null)
        {
            return NotFound("Phiếu kiểm kê không tồn tại.");
        }

        await CycleCountReconciliation.PopulateExpectedAtCountQuantitiesAsync(
            _context,
            count,
            HttpContext?.RequestAborted ?? default);
        var identityIds = new[] { count.CreatedBy, count.ApprovedBy }
            .Where(identityId => !string.IsNullOrWhiteSpace(identityId))
            .Select(identityId => identityId!)
            .Distinct()
            .ToList();
        var userNames = await _context.Users
            .AsNoTracking()
            .Where(user => identityIds.Contains(user.Id))
            .Select(user => new { user.Id, user.FullName })
            .ToDictionaryAsync(
                user => user.Id,
                user => user.FullName,
                HttpContext?.RequestAborted ?? default);
        var createdBy = ResolveDisplayName(count.CreatedBy, userNames);
        var approvedBy = ResolveDisplayName(count.ApprovedBy, userNames);

        return File(
            CreateCycleCountDocument(
                count,
                createdBy,
                approvedBy,
                FormatBusinessDate(count.CompletedAt ?? count.CreatedAt))
                .GeneratePdf(),
            "application/pdf",
            $"BienBanKiemKe_{SanitizeFileNameIdentifier(count.CountNumber)}.pdf");
    }

    [HttpGet("receipt/{id:int}")]
    [Authorize(Roles = "Admin,Warehouse,Manager")]
    public async Task<IActionResult> PrintReceipt(int id)
    {
        var receipt = await _context.GoodsReceipts
            .AsNoTracking()
            .Include(document => document.Supplier)
            .Include(document => document.Lines)
                .ThenInclude(line => line.Product)
            .Include(document => document.Lines)
                .ThenInclude(line => line.Location)
            .SingleOrDefaultAsync(document => document.Id == id);

        if (receipt is null)
        {
            return NotFound("Phiếu nhập kho không tồn tại.");
        }

        return File(
            CreateReceiptDocument(
                receipt,
                FormatBusinessDate(receipt.ReceiptDate))
                .GeneratePdf(),
            "application/pdf",
            $"PhieuNhapKho_{SanitizeFileNameIdentifier(receipt.ReceiptNo)}.pdf");
    }

    [HttpGet("issue/{id:int}")]
    [Authorize(Roles = "Admin,Warehouse,Manager")]
    public async Task<IActionResult> PrintIssue(int id)
    {
        var issue = await _context.GoodsIssues
            .AsNoTracking()
            .Include(document => document.Customer)
            .Include(document => document.Lines)
                .ThenInclude(line => line.Product)
            .Include(document => document.Lines)
                .ThenInclude(line => line.Lot)
            .Include(document => document.Lines)
                .ThenInclude(line => line.Location)
            .SingleOrDefaultAsync(document => document.Id == id);

        if (issue is null)
        {
            return NotFound("Phiếu xuất kho không tồn tại.");
        }

        return File(
            CreateIssueDocument(
                issue,
                FormatBusinessDate(issue.IssueDate))
                .GeneratePdf(),
            "application/pdf",
            $"PhieuXuatKho_{SanitizeFileNameIdentifier(issue.IssueNo)}.pdf");
    }

    private static IDocument CreateCycleCountDocument(
        CycleCountOrder count,
        string createdBy,
        string approvedBy,
        string countDate)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                ConfigureA4Page(page);
                page.Header().AlignCenter().Text(CycleCountTitle)
                    .Bold()
                    .FontSize(16)
                    .FontColor(Colors.Blue.Darken3);

                page.Content().PaddingVertical(14).Column(column =>
                {
                    column.Spacing(12);
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(85);
                            columns.RelativeColumn();
                            columns.ConstantColumn(85);
                            columns.RelativeColumn();
                        });

                        AddDetail(table, "Số phiếu", count.CountNumber);
                        AddDetail(table, "Kho", count.Warehouse?.Name ?? string.Empty);
                        AddDetail(
                            table,
                            count.CompletedAt.HasValue ? "Ngày đếm" : "Ngày lập",
                            countDate);
                        AddDetail(table, "Người tạo", createdBy);
                        AddDetail(table, "Người duyệt", approvedBy);
                    });

                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(1.05f);
                            columns.RelativeColumn(1.45f);
                            columns.RelativeColumn(0.85f);
                            columns.RelativeColumn(1.05f);
                            columns.RelativeColumn(0.65f);
                            columns.RelativeColumn(0.65f);
                            columns.RelativeColumn(0.65f);
                            columns.RelativeColumn(1.35f);
                            columns.RelativeColumn(1.05f);
                        });

                        table.Header(header =>
                        {
                            AddHeaderCell(header, "SKU");
                            AddHeaderCell(header, "Tên sản phẩm");
                            AddHeaderCell(header, "Vị trí");
                            AddHeaderCell(header, "Số lô");
                            AddHeaderCell(header, "Dự kiến lúc đếm");
                            AddHeaderCell(header, "SL thực đếm");
                            AddHeaderCell(header, "Chênh lệch");
                            AddHeaderCell(header, VarianceReasonHeader);
                            AddHeaderCell(header, "Giá trị chênh lệch (VNĐ)");
                        });

                        foreach (var item in count.Items.OrderBy(item => item.Product?.Code))
                        {
                            AddBodyCell(table, item.Product?.Code ?? string.Empty);
                            AddBodyCell(table, item.Product?.Name ?? string.Empty);
                            AddBodyCell(table, item.Location?.Code ?? string.Empty);
                            AddBodyCell(table, item.Lot?.LotNo ?? string.Empty);
                            AddNumberCell(
                                table,
                                FormatQuantity(item.ExpectedAtCountQty ?? item.SystemQty));
                            if (item.CountedQty.HasValue)
                            {
                                AddNumberCell(table, FormatQuantity(item.CountedQty.Value));
                            }
                            else
                            {
                                AddBodyCell(table, "Chưa kiểm đếm");
                            }
                            AddNumberCell(
                                table,
                                FormatQuantity(item.AuthoritativeVarianceQty));
                            AddBodyCell(table, item.ReasonNote ?? string.Empty);
                            AddNumberCell(
                                table,
                                FormatCurrency(
                                    item.AuthoritativeVarianceQty *
                                    (item.Lot?.UnitPrice ?? 0m)));
                        }
                    });

                    column.Item().PaddingTop(22).Row(row =>
                    {
                        AddSignature(row, CounterSignatureTitle);
                        AddSignature(row, AuditorSignatureTitle);
                        AddSignature(row, ApproverSignatureTitle);
                    });
                });

                AddPageFooter(page);
            });
        });
    }

    private static IDocument CreateReceiptDocument(
        GoodsReceipt receipt,
        string receiptDate)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                ConfigureA4Page(page);
                page.Header().AlignCenter().Text(ReceiptTitle)
                    .Bold()
                    .FontSize(18)
                    .FontColor(Colors.Blue.Darken3);

                page.Content().PaddingVertical(14).Column(column =>
                {
                    column.Spacing(12);
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(90);
                            columns.RelativeColumn();
                            columns.ConstantColumn(90);
                            columns.RelativeColumn();
                        });

                        AddDetail(table, "Số phiếu", receipt.ReceiptNo);
                        AddDetail(table, "Ngày nhập", receiptDate);
                        AddDetail(table, "Nhà cung cấp", receipt.Supplier?.Name ?? string.Empty);
                        AddDetail(table, "Trạng thái", receipt.Status.ToString());
                    });

                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(24);
                            columns.RelativeColumn(1.1f);
                            columns.RelativeColumn(1.5f);
                            columns.RelativeColumn(1.1f);
                            columns.RelativeColumn(0.9f);
                            columns.RelativeColumn(0.7f);
                            columns.RelativeColumn(0.9f);
                            columns.RelativeColumn(1.7f);
                        });

                        table.Header(header =>
                        {
                            AddHeaderCell(header, "STT");
                            AddHeaderCell(header, "SKU");
                            AddHeaderCell(header, "Tên vật tư");
                            AddHeaderCell(header, "Số lô");
                            AddHeaderCell(header, "Vị trí");
                            AddHeaderCell(header, "Số lượng");
                            AddHeaderCell(header, "Đơn giá");
                            AddHeaderCell(header, VarianceReasonHeader);
                        });

                        var index = 1;
                        foreach (var line in receipt.Lines.OrderBy(line => line.Id))
                        {
                            AddNumberCell(table, index++.ToString(CultureInfo.InvariantCulture));
                            AddBodyCell(table, line.Product?.Code ?? string.Empty);
                            AddBodyCell(table, line.Product?.Name ?? string.Empty);
                            AddBodyCell(table, line.LotNo);
                            AddBodyCell(table, line.Location?.Code ?? string.Empty);
                            AddNumberCell(table, FormatQuantity(line.Qty));
                            AddNumberCell(table, FormatCurrency(line.UnitPrice));
                            AddBodyCell(table, line.VarianceReason ?? string.Empty);
                        }
                    });
                });

                AddPageFooter(page);
            });
        });
    }

    private static IDocument CreateIssueDocument(
        GoodsIssue issue,
        string issueDate)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                ConfigureA4Page(page);
                page.Header().AlignCenter().Text(IssueTitle)
                    .Bold()
                    .FontSize(18)
                    .FontColor(Colors.Blue.Darken3);

                page.Content().PaddingVertical(14).Column(column =>
                {
                    column.Spacing(12);
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(90);
                            columns.RelativeColumn();
                            columns.ConstantColumn(90);
                            columns.RelativeColumn();
                        });

                        AddDetail(table, "Số phiếu", issue.IssueNo);
                        AddDetail(table, "Ngày xuất", issueDate);
                        AddDetail(table, "Khách hàng", issue.Customer?.Name ?? string.Empty);
                        AddDetail(table, "Trạng thái", issue.Status.ToString());
                    });

                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(24);
                            columns.RelativeColumn(1.1f);
                            columns.RelativeColumn(1.5f);
                            columns.RelativeColumn(1.1f);
                            columns.RelativeColumn(0.9f);
                            columns.RelativeColumn(0.7f);
                            columns.RelativeColumn(1.8f);
                        });

                        table.Header(header =>
                        {
                            AddHeaderCell(header, "STT");
                            AddHeaderCell(header, "SKU");
                            AddHeaderCell(header, "Tên vật tư");
                            AddHeaderCell(header, "Số lô");
                            AddHeaderCell(header, "Vị trí");
                            AddHeaderCell(header, "Số lượng");
                            AddHeaderCell(header, VarianceReasonHeader);
                        });

                        var index = 1;
                        foreach (var line in issue.Lines.OrderBy(line => line.Id))
                        {
                            AddNumberCell(table, index++.ToString(CultureInfo.InvariantCulture));
                            AddBodyCell(table, line.Product?.Code ?? string.Empty);
                            AddBodyCell(table, line.Product?.Name ?? string.Empty);
                            AddBodyCell(table, line.Lot?.LotNo ?? string.Empty);
                            AddBodyCell(table, line.Location?.Code ?? string.Empty);
                            AddNumberCell(table, FormatQuantity(line.Qty));
                            AddBodyCell(table, line.VarianceReason ?? string.Empty);
                        }
                    });
                });

                AddPageFooter(page);
            });
        });
    }

    private static void ConfigureA4Page(PageDescriptor page)
    {
        page.Size(PageSizes.A4);
        page.Margin(24);
        page.DefaultTextStyle(style => style.FontFamily(PdfFontRegistration.FontFamilyName).FontSize(8));
    }

    private static void AddDetail(TableDescriptor table, string label, string value)
    {
        table.Cell().Element(DetailLabelCell).Text(label);
        table.Cell().Element(DetailValueCell).Text(value);
    }

    private static void AddHeaderCell(TableCellDescriptor header, string text)
    {
        header.Cell().Element(TableHeaderCell).AlignCenter().Text(text);
    }

    private static void AddBodyCell(TableDescriptor table, string text)
    {
        table.Cell().Element(TableBodyCell).Text(text);
    }

    private static void AddNumberCell(TableDescriptor table, string text)
    {
        table.Cell().Element(TableBodyCell).AlignRight().Text(text);
    }

    private static void AddSignature(RowDescriptor row, string title)
    {
        row.RelativeItem().AlignCenter().Column(column =>
        {
            column.Item().AlignCenter().Text(title).SemiBold();
            column.Item().PaddingTop(42).AlignCenter().Text("(Ký và ghi rõ họ tên)").Italic().FontSize(7);
        });
    }

    private static void AddPageFooter(PageDescriptor page)
    {
        page.Footer().AlignCenter().Text(text =>
        {
            text.Span("Trang ");
            text.CurrentPageNumber();
            text.Span(" / ");
            text.TotalPages();
        });
    }

    private static string FormatQuantity(decimal value) =>
        value.ToString("#,##0.###", CultureInfo.GetCultureInfo("vi-VN"));

    private static string FormatCurrency(decimal value) =>
        value.ToString("#,##0", CultureInfo.GetCultureInfo("vi-VN"));

    private string FormatBusinessDate(DateTime storedUtc)
    {
        var businessDateTime = storedUtc.ToVietnameseBusinessDateTime(
            _businessTimeZone);
        return businessDateTime[..10];
    }

    private static string ResolveDisplayName(
        string? storedIdentity,
        IReadOnlyDictionary<string, string> userNames)
    {
        if (string.IsNullOrWhiteSpace(storedIdentity))
        {
            return string.Empty;
        }

        return userNames.TryGetValue(storedIdentity, out var fullName) &&
               !string.IsNullOrWhiteSpace(fullName)
            ? fullName
            : storedIdentity;
    }

    private static string SanitizeFileNameIdentifier(string identifier)
    {
        var sanitized = new StringBuilder(Math.Min(identifier.Length, 80));
        foreach (var character in identifier)
        {
            var isAllowed = character is >= 'a' and <= 'z'
                or >= 'A' and <= 'Z'
                or >= '0' and <= '9'
                or '.'
                or '_'
                or '-';
            if (isAllowed)
            {
                sanitized.Append(character);
            }
            else if (sanitized.Length > 0 && sanitized[^1] != '_')
            {
                sanitized.Append('_');
            }

            if (sanitized.Length == 80)
            {
                break;
            }
        }

        var result = sanitized.ToString().Trim('.', '_');
        return string.IsNullOrEmpty(result) ? "document" : result;
    }

    private static IContainer DetailLabelCell(IContainer container) =>
        container.Background(Colors.Grey.Lighten3)
            .Border(0.5f)
            .BorderColor(Colors.Grey.Lighten1)
            .Padding(5)
            .DefaultTextStyle(style => style.SemiBold());

    private static IContainer DetailValueCell(IContainer container) =>
        container.Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(5);

    private static IContainer TableHeaderCell(IContainer container) =>
        container.Background(Colors.Blue.Darken3)
            .Border(0.5f)
            .BorderColor(Colors.White)
            .Padding(4)
            .DefaultTextStyle(style => style.FontColor(Colors.White).SemiBold().FontSize(7));

    private static IContainer TableBodyCell(IContainer container) =>
        container.BorderBottom(0.5f)
            .BorderColor(Colors.Grey.Lighten1)
            .PaddingVertical(4)
            .PaddingHorizontal(3)
            .DefaultTextStyle(style => style.FontSize(7));

    private static byte[] GenerateQrCode(string text)
    {
        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);
        using var qrCode = new PngByteQRCode(qrCodeData);
        return qrCode.GetGraphic(20);
    }

    private static string Abbreviate(string value, int maximumLength)
    {
        return value.Length <= maximumLength
            ? value
            : $"{value[..(maximumLength - 1)]}…";
    }
}

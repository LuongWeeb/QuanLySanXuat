using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using WmsMes.Web.Data;

namespace WmsMes.Web.Controllers;

[Authorize]
[Route("api/[controller]")]
[ApiController]
public class PrintController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public PrintController(ApplicationDbContext context)
    {
        _context = context;
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
                            column.Item().Text(location.Zone?.Warehouse?.Name ?? "WMS WAREHOUSE")
                                .FontSize(8)
                                .Bold();
                            column.Item().Text($"Khu vực: {location.Zone?.Code ?? "N/A"}")
                                .FontSize(8);
                            column.Item().PaddingTop(2, Unit.Millimetre).Text(location.Code)
                                .FontSize(16)
                                .Bold()
                                .FontColor(Colors.Blue.Darken4);
                        });
                });
            });
        });

        return File(
            document.GeneratePdf(),
            "application/pdf",
            $"Label_Loc_{location.Code}.pdf");
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
                            column.Item().Text(lot.Product?.Name ?? "SẢN PHẨM")
                                .FontSize(8)
                                .Bold();
                            column.Item().Text($"SKU: {lot.Product?.Code ?? "N/A"}").FontSize(8);
                            column.Item().Text($"NSX: {lot.ManufactureDate?.ToString("dd/MM/yyyy") ?? "N/A"}")
                                .FontSize(7);
                            column.Item().Text($"HSD: {lot.ExpiryDate?.ToString("dd/MM/yyyy") ?? "N/A"}")
                                .FontSize(7);
                            column.Item().PaddingTop(1, Unit.Millimetre).Text(lot.LotNo)
                                .FontSize(14)
                                .Bold()
                                .FontColor(Colors.Green.Darken4);
                        });
                });
            });
        });

        return File(
            document.GeneratePdf(),
            "application/pdf",
            $"Label_Lot_{lot.LotNo}.pdf");
    }

    private static byte[] GenerateQrCode(string text)
    {
        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);
        using var qrCode = new PngByteQRCode(qrCodeData);
        return qrCode.GetGraphic(20);
    }
}

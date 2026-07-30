using System.Net;
using System.Text;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UglyToad.PdfPig;
using WmsMes.Web.Controllers;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Services;

namespace WmsMes.Tests;

public class PrintControllerInventoryDocumentTests :
    IClassFixture<PdfFontRegistrationFixture>
{
    public PrintControllerInventoryDocumentTests(PdfFontRegistrationFixture _)
    {
    }

    [Fact]
    public async Task PrintCycleCount_ReturnsNamedA4Pdf_WithStocktakeDetails()
    {
        var options = CreateOptions();
        await using (var context = new ApplicationDbContext(options))
        {
            var warehouse = new Warehouse { Id = 1, Code = "WH-01", Name = "Kho thanh pham" };
            var zone = new Zone { Id = 2, Code = "Z-01", Name = "Khu A", Warehouse = warehouse };
            var location = new Location { Id = 3, Code = "LOC-A-01", Name = "Ke A01", Zone = zone };
            var product = new Product
            {
                Id = 4,
                Code = "SKU-COUNT-01",
                Name = "San pham kiem ke",
                BaseUomId = 1
            };
            var lot = new Lot
            {
                Id = 5,
                LotNo = "LOT-COUNT-01",
                Product = product,
                UnitPrice = 12_500m
            };
            context.CycleCountOrders.Add(new CycleCountOrder
            {
                Id = 6,
                CountNumber = "CC/2026:* 001",
                Warehouse = warehouse,
                CreatedAt = new DateTime(2026, 7, 30),
                CompletedAt = new DateTime(2026, 7, 31),
                CreatedBy = "Nguyen Van A",
                ApprovedBy = "Tran Van B",
                Items =
                [
                    new CycleCountItem
                    {
                        Id = 7,
                        Product = product,
                        Location = location,
                        Lot = lot,
                        SystemQty = 10m,
                        CountedQty = 8m,
                        ReasonNote = "Hu hong khi luu kho"
                    }
                ]
            });
            await context.SaveChangesAsync();
        }

        await using var assertionContext = new ApplicationDbContext(options);
        var controller = new PrintController(assertionContext);

        var result = await controller.PrintCycleCount(6);

        var file = AssertPdf(result, "BienBanKiemKe_CC_2026_001.pdf");
        var text = ReadSingleA4PageText(file.FileContents);
        AssertControllerLabel("CycleCountTitle", "BIÊN BẢN KIỂM KÊ VÀ ĐỐI CHIẾU TỒN KHO");
        AssertControllerLabel("VarianceReasonHeader", "Lý do chênh lệch");
        AssertControllerLabel("CounterSignatureTitle", "Người kiểm đếm (Thủ kho)");
        AssertControllerLabel("AuditorSignatureTitle", "Nhân viên Kiểm toán/QC");
        AssertControllerLabel("ApproverSignatureTitle", "Trưởng kho/Giám đốc duyệt");
        Assert.Contains("BIÊN BẢN KIỂM KÊ VÀ ĐỐI CHIẾU TỒN KHO", text);
        Assert.Contains("CC/2026:* 001", text);
        Assert.Contains("Kho thanh pham", text);
        Assert.Contains("31/07/2026", text);
        Assert.DoesNotContain("30/07/2026", text);
        Assert.Contains("SKU-COUNT-01", text);
        Assert.Contains("LOT-COUNT-01", text);
        Assert.Contains("Hu hong khi luu kho", text);
        Assert.Contains("-25.000", text);
        Assert.Contains("Lý do chênh lệch", text);
        Assert.Contains("Người kiểm đếm (Thủ kho)", text);
        Assert.Contains("Nhân viên Kiểm toán/QC", text);
        Assert.Contains("Trưởng kho/Giám đốc duyệt", text);
    }

    [Fact]
    public async Task PrintReceipt_ReturnsNamedA4Pdf_WithVarianceReason()
    {
        var options = CreateOptions();
        await using (var context = new ApplicationDbContext(options))
        {
            var product = new Product
            {
                Id = 10,
                Code = "SKU-RECEIPT-01",
                Name = "Vat tu nhap kho",
                BaseUomId = 1
            };
            var location = CreateLocation(11, "LOC-R-01");
            context.GoodsReceipts.Add(new GoodsReceipt
            {
                Id = 12,
                ReceiptNo = "GR/2026:* 001",
                ReceiptDate = new DateTime(2026, 7, 29),
                Supplier = new Supplier { Id = 13, Code = "SUP-01", Name = "Nha cung cap A" },
                Lines =
                [
                    new GoodsReceiptLine
                    {
                        Id = 14,
                        Product = product,
                        Location = location,
                        LotNo = "LOT-RECEIPT-01",
                        Qty = 25m,
                        UnitPrice = 20_000m,
                        VarianceReason = "Thua theo bien ban giao nhan"
                    }
                ]
            });
            await context.SaveChangesAsync();
        }

        await using var assertionContext = new ApplicationDbContext(options);
        var controller = new PrintController(assertionContext);

        var result = await controller.PrintReceipt(12);

        var file = AssertPdf(result, "PhieuNhapKho_GR_2026_001.pdf");
        var text = ReadSingleA4PageText(file.FileContents);
        AssertControllerLabel("ReceiptTitle", "PHIẾU NHẬP KHO");
        AssertControllerLabel("VarianceReasonHeader", "Lý do chênh lệch");
        Assert.Contains("PHIẾU NHẬP KHO", text);
        Assert.Contains("SKU-RECEIPT-01", text);
        Assert.Contains("Lý do chênh lệch", text);
        Assert.Contains("Thua theo bien ban giao nhan", text);
    }

    [Fact]
    public async Task PrintIssue_ReturnsNamedA4Pdf_WithVarianceReason()
    {
        var options = CreateOptions();
        await using (var context = new ApplicationDbContext(options))
        {
            var product = new Product
            {
                Id = 20,
                Code = "SKU-ISSUE-01",
                Name = "Vat tu xuat kho",
                BaseUomId = 1
            };
            var lot = new Lot { Id = 21, LotNo = "LOT-ISSUE-01", Product = product };
            var location = CreateLocation(22, "LOC-I-01");
            context.GoodsIssues.Add(new GoodsIssue
            {
                Id = 23,
                IssueNo = "GI/2026:* 001",
                IssueDate = new DateTime(2026, 7, 28),
                Customer = new Customer { Id = 24, Code = "CUS-01", Name = "Khach hang A" },
                Lines =
                [
                    new GoodsIssueLine
                    {
                        Id = 25,
                        Product = product,
                        Lot = lot,
                        Location = location,
                        Qty = 5m,
                        VarianceReason = "Thieu do kiem dem"
                    }
                ]
            });
            await context.SaveChangesAsync();
        }

        await using var assertionContext = new ApplicationDbContext(options);
        var controller = new PrintController(assertionContext);

        var result = await controller.PrintIssue(23);

        var file = AssertPdf(result, "PhieuXuatKho_GI_2026_001.pdf");
        var text = ReadSingleA4PageText(file.FileContents);
        AssertControllerLabel("IssueTitle", "PHIẾU XUẤT KHO");
        AssertControllerLabel("VarianceReasonHeader", "Lý do chênh lệch");
        Assert.Contains("PHIẾU XUẤT KHO", text);
        Assert.Contains("SKU-ISSUE-01", text);
        Assert.Contains("Lý do chênh lệch", text);
        Assert.Contains("Thieu do kiem dem", text);
    }

    [Fact]
    public async Task PrintCycleCount_UsesUnfinishedDateLabel_LeavesNullCountBlank_AndPaginatesLongReasons()
    {
        var options = CreateOptions();
        await using (var context = new ApplicationDbContext(options))
        {
            var warehouse = new Warehouse { Id = 100, Code = "WH-LONG", Name = "Kho kiem ke dai" };
            var zone = new Zone { Id = 101, Code = "ZONE-LONG", Name = "Khu dai", Warehouse = warehouse };
            var location = new Location { Id = 102, Code = "LOC-LONG", Name = "Ke dai", Zone = zone };
            var product = new Product
            {
                Id = 103,
                Code = "SKU-LONG",
                Name = "San pham nhieu dong",
                BaseUomId = 1
            };
            var lot = new Lot
            {
                Id = 104,
                LotNo = "LOT-LONG",
                Product = product,
                UnitPrice = 1_000m
            };
            var reason = string.Concat(Enumerable.Repeat("Ly do chenhlech chi tiet ", 11))[..250];
            Assert.Equal(250, reason.Length);
            var order = new CycleCountOrder
            {
                Id = 105,
                CountNumber = "CC-LONG-001",
                Warehouse = warehouse,
                CreatedAt = new DateTime(2026, 7, 27),
                CreatedBy = "Warehouse User"
            };
            for (var index = 1; index <= 18; index++)
            {
                order.Items.Add(new CycleCountItem
                {
                    Id = 105 + index,
                    Product = product,
                    Location = location,
                    Lot = lot,
                    SystemQty = index == 1 ? 9_876m : 1_000m + index,
                    CountedQty = null,
                    ReasonNote = reason
                });
            }

            context.CycleCountOrders.Add(order);
            await context.SaveChangesAsync();
        }

        await using var assertionContext = new ApplicationDbContext(options);
        var controller = new PrintController(assertionContext);

        var file = AssertPdf(
            await controller.PrintCycleCount(105),
            "BienBanKiemKe_CC-LONG-001.pdf");

        using var document = PdfDocument.Open(file.FileContents);
        var pages = document.GetPages().ToList();
        Assert.True(pages.Count > 1);
        Assert.All(pages, page => Assert.Contains("Lý do chênh lệch", page.Text));
        Assert.All(pages, page => Assert.Contains("SKU-LONG", page.Text));
        Assert.Contains("Ngày lập", pages[0].Text);
        Assert.DoesNotContain("Ngày đếm", pages[0].Text);
        Assert.Contains("27/07/2026", pages[0].Text);

        var words = pages.SelectMany(page => page.GetWords())
            .Select(word => word.Text)
            .ToList();
        Assert.Contains("Chưa", words);
        Assert.Contains("kiểm", words);
        Assert.Contains("đếm", words);
        Assert.Single(words.Where(word => word == "9.876"));
        Assert.DoesNotContain(CounterSignatureTitle, pages[0].Text);
        Assert.Contains(CounterSignatureTitle, pages[^1].Text);
        Assert.Contains(AuditorSignatureTitle, pages[^1].Text);
        Assert.Contains(ApproverSignatureTitle, pages[^1].Text);
    }

    [Fact]
    public void InventoryPrintActions_RequireWarehouseRoles()
    {
        foreach (var actionName in new[]
                 {
                     nameof(PrintController.PrintCycleCount),
                     nameof(PrintController.PrintReceipt),
                     nameof(PrintController.PrintIssue)
                 })
        {
            var action = typeof(PrintController).GetMethod(actionName);
            Assert.NotNull(action);
            var authorize = Assert.Single(
                action.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
                    .Cast<AuthorizeAttribute>());
            Assert.Equal("Admin,Warehouse,Manager", authorize.Roles);
        }
    }

    [Fact]
    public void PortablePdfFontAndLicense_AreCopiedToApplicationOutput()
    {
        var fontDirectory = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts");
        var fontPath = Path.Combine(fontDirectory, "NotoSans-Variable.ttf");
        var licensePath = Path.Combine(fontDirectory, "OFL.txt");

        Assert.True(File.Exists(fontPath), $"Bundled PDF font missing: {fontPath}");
        Assert.True(File.Exists(licensePath), $"Bundled font license missing: {licensePath}");
        Assert.Contains(
            "SIL OPEN FONT LICENSE Version 1.1",
            File.ReadAllText(licensePath),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("cyclecount", "Phiếu kiểm kê không tồn tại.")]
    [InlineData("receipt", "Phiếu nhập kho không tồn tại.")]
    [InlineData("issue", "Phiếu xuất kho không tồn tại.")]
    public async Task PrintInventoryDocument_ReturnsNotFound_WithMessage_WhenMissing(
        string documentType,
        string expectedMessage)
    {
        await using var context = new ApplicationDbContext(CreateOptions());
        var controller = new PrintController(context);

        var result = documentType switch
        {
            "cyclecount" => await controller.PrintCycleCount(404),
            "receipt" => await controller.PrintReceipt(404),
            _ => await controller.PrintIssue(404)
        };

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal(expectedMessage, Assert.IsType<string>(notFound.Value));
    }

    private static DbContextOptions<ApplicationDbContext> CreateOptions()
    {
        return new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
    }

    private static Location CreateLocation(int id, string code)
    {
        var warehouse = new Warehouse { Id = id + 100, Code = $"WH-{id}", Name = $"Kho {id}" };
        var zone = new Zone { Id = id + 200, Code = $"Z-{id}", Name = $"Khu {id}", Warehouse = warehouse };
        return new Location { Id = id, Code = code, Name = code, Zone = zone };
    }

    private static FileContentResult AssertPdf(IActionResult result, string expectedFileName)
    {
        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal(expectedFileName, file.FileDownloadName);
        Assert.Matches("^[A-Za-z0-9._-]+$", file.FileDownloadName);
        Assert.NotEmpty(file.FileContents);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(file.FileContents, 0, 5));
        return file;
    }

    private static string ReadSingleA4PageText(byte[] bytes)
    {
        using var document = PdfDocument.Open(bytes);
        var page = Assert.Single(document.GetPages());
        Assert.InRange(page.Width, 595.0, 596.0);
        Assert.InRange(page.Height, 841.5, 843.0);
        return page.Text;
    }

    private static void AssertControllerLabel(string fieldName, string expectedValue)
    {
        var field = typeof(PrintController).GetField(
            fieldName,
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        Assert.Equal(expectedValue, field.GetRawConstantValue());
    }

    private const string CounterSignatureTitle = "Người kiểm đếm (Thủ kho)";
    private const string AuditorSignatureTitle = "Nhân viên Kiểm toán/QC";
    private const string ApproverSignatureTitle = "Trưởng kho/Giám đốc duyệt";
}

public sealed class PdfFontRegistrationFixture
{
    public PdfFontRegistrationFixture()
    {
        PdfFontRegistration.RegisterFromAppBaseDirectory();
    }
}

public class PrintDocumentAuthorizationTests :
    IClassFixture<InventoryCancellationWebApplicationFactory>
{
    private readonly InventoryCancellationWebApplicationFactory _factory;

    public PrintDocumentAuthorizationTests(InventoryCancellationWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("/api/print/cyclecount/404", HttpStatusCode.NotFound)]
    [InlineData("/api/print/receipt/101", HttpStatusCode.OK)]
    [InlineData("/api/print/issue/201", HttpStatusCode.OK)]
    public async Task InventoryPrintRoutes_EnforceRoles_AndAllowWarehouse(
        string route,
        HttpStatusCode allowedStatus)
    {
        using var anonymousClient = _factory.CreateInventoryClient();
        using var forbiddenClient = _factory.CreateInventoryClient("Worker");
        using var warehouseClient = _factory.CreateInventoryClient("Warehouse");

        var anonymousResponse = await anonymousClient.GetAsync(route);
        var forbiddenResponse = await forbiddenClient.GetAsync(route);
        var warehouseResponse = await warehouseClient.GetAsync(route);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);
        Assert.Equal(allowedStatus, warehouseResponse.StatusCode);
        if (allowedStatus == HttpStatusCode.OK)
        {
            Assert.Equal(
                "application/pdf",
                warehouseResponse.Content.Headers.ContentType?.MediaType);
        }
    }
}

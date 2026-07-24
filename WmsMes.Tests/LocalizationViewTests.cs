namespace WmsMes.Tests;

public class LocalizationViewTests
{
    [Fact]
    public void Views_ImportDisplayExtensionsAndUseThemForDomainEnums()
    {
        Assert.Contains("@using WmsMes.Web.Domain.Common", ReadView("_ViewImports.cshtml"));

        Assert.Contains("@item.Type.ToVietnameseString()", ReadView("Product", "Index.cshtml"));
        Assert.Contains("@order.Status.ToVietnameseString()", ReadView("WorkOrder", "Index.cshtml"));
        Assert.Contains("@Model.Status.ToVietnameseString()", ReadView("WorkOrder", "Details.cshtml"));
        Assert.Contains("@step.Status.ToVietnameseString()", ReadView("WorkOrder", "Details.cshtml"));
        Assert.Contains("@step.Status.ToVietnameseString()", ReadView("Worker", "Index.cshtml"));
        Assert.Contains("@workOrder.Status.ToVietnameseString()", ReadView("Home", "Search.cshtml"));
        Assert.Contains("@QCResult.PASS.ToVietnameseString()", ReadView("Qc", "Inspect.cshtml"));
        Assert.Contains("@QCResult.REJECT.ToVietnameseString()", ReadView("Qc", "Inspect.cshtml"));
    }

    [Fact]
    public void Views_UseExplicitVietnameseHelpersForHumanReadableNumbersAndDates()
    {
        var allViews = ReadAllViews();

        Assert.DoesNotContain(".ToString(\"N", allViews);
        Assert.DoesNotContain(".ToString(\"yyyy-MM-dd HH:mm\")", allViews);

        Assert.Contains("@item.MinStock.ToVietnameseNumber()", ReadView("Product", "Index.cshtml"));
        Assert.Contains("item.Lot?.ExpiryDate?.ToVietnameseDate()", ReadView("Inventory", "Index.cshtml"));
        Assert.Contains("@receipt.ReceiptDate.ToVietnameseDateTime()", ReadView("Inventory", "Receipts.cshtml"));
        Assert.Contains("@issue.IssueDate.ToVietnameseDateTime()", ReadView("Inventory", "Issues.cshtml"));
        Assert.Contains("@order.DueDate.ToVietnameseDate()", ReadView("WorkOrder", "Index.cshtml"));
        Assert.Contains("@log.Date.ToVietnameseDate()", ReadView("WorkOrder", "Details.cshtml"));
        Assert.Contains("@bom.EffectiveDate.ToVietnameseDate()", ReadView("Bom", "Index.cshtml"));
        Assert.Contains("@Model.EffectiveDate.ToVietnameseDate()", ReadView("Bom", "Details.cshtml"));
        Assert.Contains("@Model.OverallOeePercent.ToVietnameseNumber(\"N1\")%", ReadView("Home", "Index.cshtml"));
        Assert.Contains("@Model.LowStockAlertCount.ToVietnameseNumber()", ReadView("Home", "Index.cshtml"));
        Assert.Contains("@daysRemaining.ToVietnameseNumber()", ReadView("WorkOrder", "Index.cshtml"));
        Assert.Contains("@step.StepNumber.ToVietnameseNumber()", ReadView("Worker", "Index.cshtml"));
        Assert.Contains("@zone.Locations.Count.ToVietnameseNumber()", ReadView("Warehouse", "Index.cshtml"));
        Assert.Contains("minimumFractionDigits: 2, maximumFractionDigits: 2", ReadView("Traceability", "Index.cshtml"));
        Assert.Contains("number.format(node.qty)", ReadView("Traceability", "Index.cshtml"));

        var dashboard = ReadView("Home", "Index.cshtml");
        Assert.Contains("minimumFractionDigits: 0, maximumFractionDigits: 0", dashboard);
        Assert.Contains("minimumFractionDigits: 1, maximumFractionDigits: 1", dashboard);
        Assert.Contains("minimumFractionDigits: 2, maximumFractionDigits: 2", dashboard);
        Assert.Contains("integerNumber.format(metrics.activeWorkOrders)", dashboard);
        Assert.Contains("decimalNumber.format(metrics.inventoryVolume)", dashboard);
        Assert.Contains("percentNumber", dashboard);
    }

    [Fact]
    public void Views_PreserveInvariantMachineValuesForInputsProgressAndIdentifiers()
    {
        var createReceipt = ReadView("Inventory", "CreateReceipt.cshtml");
        var createIssue = ReadView("Inventory", "CreateIssue.cshtml");
        var workOrderIndex = ReadView("WorkOrder", "Index.cshtml");
        var workOrderDetails = ReadView("WorkOrder", "Details.cshtml");

        Assert.Contains("line.Qty.ToString(System.Globalization.CultureInfo.InvariantCulture)", createReceipt);
        Assert.Contains("line.UnitPrice.ToString(System.Globalization.CultureInfo.InvariantCulture)", createReceipt);
        Assert.Contains("line.Qty.ToString(System.Globalization.CultureInfo.InvariantCulture)", createIssue);
        Assert.Contains("progressWidth.ToString(\"0.##\", CultureInfo.InvariantCulture)", workOrderIndex);
        Assert.Contains("progressWidth.ToString(\"0.##\", CultureInfo.InvariantCulture)", workOrderDetails);
        Assert.Contains("businessDate.ToString(\"yyyy-MM-dd\")", workOrderDetails);
        Assert.Contains("type=\"date\"", workOrderDetails);
        Assert.Contains("type=\"number\"", workOrderDetails);

        var mrp = ReadView("Mrp", "Index.cshtml");
        Assert.Contains("qty.ToString(CultureInfo.InvariantCulture)", mrp);
        Assert.Contains("value=\"@qtyValue\"", mrp);
    }

    [Fact]
    public void Views_ContainVietnameseUserFacingLabelsWithoutKnownEnglishLeftovers()
    {
        var allViews = ReadAllViews();
        var forbiddenVisibleEnglish = new[]
        {
            "Master data",
            ">Lot<",
            "Raw material",
            "Finished good",
            "Storage map",
            "warehouse, zone",
            "Realtime inventory",
            "Inventory summary",
            "Work Order",
            "Global search",
            "Lot traceability",
            "Nhap so lo",
            "Quality Control",
            ">PASS -",
            ">REJECT -",
            "On Hold",
            "\"Pass\", \"Hold\", \"Quarantine\"",
            "Privacy Policy",
            "Use this page to detail",
            ">Error.<",
            "An error occurred",
            "Secure operations console",
            "Foundation phase",
            "Bill of Materials",
            "Goods Receipt",
            "Goods Issue",
            "aria-label=\"Main navigation\"",
            ">Dashboard<",
            "<small>Operations</small>"
        };

        foreach (var phrase in forbiddenVisibleEnglish)
            Assert.DoesNotContain(phrase, allViews, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("Dữ liệu danh mục", ReadView("Product", "Index.cshtml"));
        Assert.Contains("Sơ đồ kho", ReadView("Warehouse", "Index.cshtml"));
        Assert.Contains("Tồn kho thực tế", ReadView("Inventory", "Index.cshtml"));
        Assert.Contains("aria-label=\"Tóm tắt tồn kho\"", ReadView("Inventory", "Index.cshtml"));
        Assert.Contains("Truy xuất nguồn gốc lô", ReadView("Traceability", "Index.cshtml"));
        Assert.Contains("placeholder=\"Nhập số lô\"", ReadView("Traceability", "Index.cshtml"));
        Assert.Contains("Kiểm soát chất lượng", ReadView("Qc", "Index.cshtml"));
        Assert.Contains("ToVietnameseString() - Giải phóng lô", ReadView("Qc", "Inspect.cshtml"));
        Assert.Contains("ToVietnameseString() - Cách ly lô", ReadView("Qc", "Inspect.cshtml"));
        Assert.Contains("\"Đạt\", \"Tạm giữ\", \"Cách ly\"", ReadView("Home", "Index.cshtml"));
        Assert.Contains("Chính sách quyền riêng tư", ReadView("Home", "Privacy.cshtml"));
        Assert.Contains("Đã xảy ra lỗi khi xử lý yêu cầu của bạn.", ReadView("Shared", "Error.cshtml"));
    }

    private static string ReadAllViews() =>
        string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(Path.Combine(ProjectRoot(), "Views"), "*.cshtml", SearchOption.AllDirectories)
                .Select(File.ReadAllText));

    private static string ReadView(params string[] path) =>
        File.ReadAllText(Path.Combine(new[] { ProjectRoot(), "Views" }.Concat(path).ToArray()));

    private static string ProjectRoot() => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
}

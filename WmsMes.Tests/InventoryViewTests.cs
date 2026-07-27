namespace WmsMes.Tests;

public class InventoryViewTests
{
    [Theory]
    [InlineData("CreateReceipt.cshtml", "CreateReceiptViewModel", "receipt-lines", "receipt-line-template")]
    [InlineData("CreateIssue.cshtml", "CreateIssueViewModel", "issue-lines", "issue-line-template")]
    public void MultiLineForms_UseIndexedRowsAndReindexAfterDeletion(
        string fileName,
        string modelName,
        string rowsId,
        string templateId)
    {
        var view = File.ReadAllText(Path.Combine(ProjectRoot(), "Views", "Inventory", fileName));

        Assert.Contains($"@model {modelName}", view);
        Assert.Contains($"id=\"{rowsId}\"", view);
        Assert.Contains($"id=\"{templateId}\"", view);
        Assert.Contains("data-line-row", view);
        Assert.Contains("data-remove-line", view);
        Assert.Contains("reindexLines", view);
        Assert.Contains("Lines[${index}]", view);
        Assert.Contains("rows.length === 1", view);
        Assert.Contains("Thêm dòng", view);
        Assert.Contains("Xóa dòng", view);
    }

    [Fact]
    public void ManualNumericInputs_PreserveRawModelStateAttemptedValuesWithInvariantFallbacks()
    {
        var receipt = File.ReadAllText(Path.Combine(
            ProjectRoot(),
            "Views",
            "Inventory",
            "CreateReceipt.cshtml"));
        var issue = File.ReadAllText(Path.Combine(
            ProjectRoot(),
            "Views",
            "Inventory",
            "CreateIssue.cshtml"));

        Assert.Contains("ViewData.ModelState.TryGetValue(qtyKey", receipt);
        Assert.Contains("qtyEntry.AttemptedValue", receipt);
        Assert.Contains("value=\"@qtyValue\"", receipt);
        Assert.Contains("ViewData.ModelState.TryGetValue(unitPriceKey", receipt);
        Assert.Contains("unitPriceEntry.AttemptedValue", receipt);
        Assert.Contains("value=\"@unitPriceValue\"", receipt);
        Assert.Contains(
            "line.Qty.ToString(System.Globalization.CultureInfo.InvariantCulture)",
            receipt);
        Assert.Contains(
            "line.UnitPrice.ToString(System.Globalization.CultureInfo.InvariantCulture)",
            receipt);

        Assert.Contains("ViewData.ModelState.TryGetValue(qtyKey", issue);
        Assert.Contains("qtyEntry.AttemptedValue", issue);
        Assert.Contains("value=\"@qtyValue\"", issue);
        Assert.Contains(
            "line.Qty.ToString(System.Globalization.CultureInfo.InvariantCulture)",
            issue);
    }

    [Theory]
    [InlineData("CreateReceipt.cshtml")]
    [InlineData("CreateIssue.cshtml")]
    public void TransactionForms_ProvideHardwareAndCameraBarcodeScanning(string fileName)
    {
        var view = ReadInventoryView(fileName);

        Assert.Contains("id=\"barcode-scanner-input\"", view);
        Assert.Contains("id=\"btn-camera-scan\"", view);
        Assert.Contains("id=\"cameraScanModal\"", view);
        Assert.Contains("id=\"reader\"", view);
        Assert.Contains("id=\"scan-status\"", view);
        Assert.Contains("aria-live=\"polite\"", view);
        Assert.Contains("https://unpkg.com/html5-qrcode", view);
        Assert.Contains("new Html5Qrcode(\"reader\")", view);
        Assert.Contains("processScan", view);
        Assert.Contains("event.key === 'Enter'", view);
        Assert.Contains("shown.bs.modal", view);
        Assert.Contains("hidden.bs.modal", view);
        Assert.Contains("let cameraSession = 0", view);
        Assert.Contains("let scanHandled = false", view);
        Assert.Contains("const scanner = new Html5Qrcode(\"reader\")", view);
        Assert.Contains("session !== cameraSession", view);
        Assert.Contains("camera === scanner", view);
    }

    [Fact]
    public void ReceiptScanner_MapsSkuLotAndLocationToTheCurrentLine()
    {
        var view = ReadInventoryView("CreateReceipt.cshtml");

        Assert.Contains("const productsMap", view);
        Assert.Contains("const locationsMap", view);
        Assert.Contains("[data-field=\"ProductId\"]", view);
        Assert.Contains("[data-field=\"LotNo\"]", view);
        Assert.Contains("[data-field=\"LocationId\"]", view);
    }

    [Fact]
    public void IssueScanner_UsesExactStockMetadataAndUpdatesHiddenInputs()
    {
        var view = ReadInventoryView("CreateIssue.cshtml");

        Assert.Contains("data-product-code=", view);
        Assert.Contains("data-lot-no=", view);
        Assert.Contains("data-location-code=", view);
        Assert.Contains("option.dataset.productCode", view);
        Assert.Contains("selection.dispatchEvent(new Event('change'", view);
        Assert.Contains("row.dataset.scanProductCode", view);
        Assert.Contains("row.dataset.scanLotNo", view);
        Assert.Contains("row.dataset.scanLocationCode", view);
        Assert.Contains("candidates.length === 1", view);
        Assert.Contains("candidates.length > 1", view);
        Assert.Contains("option.hidden = false", view);
    }

    [Theory]
    [InlineData(
        "Receipts.cshtml",
        "receipt.Status == DocumentStatus.Completed",
        "CancelReceipt",
        "phiếu nhập kho",
        "Số lượng tồn kho sẽ bị trừ hoàn lại")]
    [InlineData(
        "Issues.cshtml",
        "issue.Status == DocumentStatus.Completed",
        "CancelIssue",
        "phiếu xuất kho",
        "Số lượng tồn kho sẽ được trả lại")]
    public void DocumentLists_RenderSecureCancellationOnlyForCompletedDocuments(
        string fileName,
        string completedCondition,
        string action,
        string documentLabel,
        string confirmation)
    {
        var view = ReadInventoryView(fileName);

        Assert.Contains(completedCondition, view);
        Assert.Contains($"asp-action=\"{action}\"", view);
        Assert.Contains("method=\"post\"", view);
        Assert.Contains("asp-antiforgery=\"true\"", view);
        Assert.DoesNotContain("@Html.AntiForgeryToken()", view);
        Assert.Contains(confirmation, view);
        Assert.Contains(">Hủy phiếu</button>", view);
        Assert.Contains("DocumentStatus.Cancelled", view);
        Assert.Contains("badge bg-danger", view);
        Assert.Contains("Đã hủy", view);
        Assert.Contains("TempData[\"ErrorMessage\"]", view);
        Assert.Contains(documentLabel, view);
    }

    [Fact]
    public void Transactions_RendersRunningBalanceValuationAndCancellationStatus()
    {
        var view = ReadInventoryView("Transactions.cshtml");

        Assert.Contains("@model WmsMes.Web.ViewModels.StockTransactionPageViewModel", view);
        Assert.Contains("@foreach (var transaction in Model.Items)", view);
        Assert.Contains("Số dư sau GD", view);
        Assert.Contains("@transaction.QtyAfter.ToVietnameseNumber()", view);
        Assert.Contains("Đơn giá vốn", view);
        Assert.Contains("@transaction.ValuationRate.ToVietnameseNumber() VNĐ", view);
        Assert.Contains("transaction.IsCancelled ? \"text-muted text-decoration-line-through\"", view);
        Assert.Contains("badge bg-danger", view);
        Assert.Contains("Đã hủy", view);
        Assert.Contains("badge bg-success", view);
        Assert.Contains("Hợp lệ", view);
        Assert.Contains("aria-label=\"Phân trang sổ cái kho\"", view);
        Assert.Contains("asp-route-beforeDate", view);
        Assert.Contains("asp-route-beforeId", view);
        Assert.Contains(">Cũ hơn</a>", view);
        Assert.Contains(">Mới nhất</a>", view);
        Assert.DoesNotContain("else if (Model.HasNextPage || !Model.IsFirstPage)", view);
    }

    private static string ReadInventoryView(string fileName) =>
        File.ReadAllText(Path.Combine(ProjectRoot(), "Views", "Inventory", fileName));

    private static string ProjectRoot() => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
}

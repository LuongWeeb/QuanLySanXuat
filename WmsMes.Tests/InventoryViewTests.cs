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
    }

    private static string ReadInventoryView(string fileName) =>
        File.ReadAllText(Path.Combine(ProjectRoot(), "Views", "Inventory", fileName));

    private static string ProjectRoot() => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
}

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

    private static string ProjectRoot() => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
}

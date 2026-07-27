using System.Text.RegularExpressions;

namespace WmsMes.Tests;

public class BomViewTests
{
    [Fact]
    public void Index_ProvidesResponsiveEmptyStateDetailsAndPostToggle()
    {
        var view = ReadView("Index.cshtml");

        Assert.Contains("@model IEnumerable<WmsMes.Web.Domain.Entities.BOM>", view);
        Assert.Contains("Quản lý Định mức vật tư", view);
        Assert.Contains("table-responsive", view);
        Assert.Contains("Chưa có định mức vật tư", view);
        Assert.Contains("asp-action=\"Details\"", view);
        Assert.Matches(
            new Regex("<form[^>]+asp-action=\"ToggleActive\"[^>]+method=\"post\"", RegexOptions.IgnoreCase),
            view);
        Assert.Contains("<button", view);
        Assert.Contains("Kích hoạt", view);
        Assert.Contains("Ngừng kích hoạt", view);
    }

    [Fact]
    public void Details_ShowsParentStatusAndResponsiveComponentTable()
    {
        var view = ReadView("Details.cshtml");

        Assert.Contains("@model WmsMes.Web.Domain.Entities.BOM", view);
        Assert.Contains("Thành phẩm / Bán thành phẩm", view);
        Assert.Contains("Phiên bản", view);
        Assert.Contains("Ngày hiệu lực", view);
        Assert.Contains("Trạng thái", view);
        Assert.Contains("Mã SKU", view);
        Assert.Contains("Định mức", view);
        Assert.Contains("Hao hụt", view);
        Assert.Contains("table-responsive", view);
        Assert.Contains("asp-action=\"Index\"", view);
    }

    [Fact]
    public void Details_ShowsVietnameseFormattedStandardCostSummary()
    {
        var view = ReadView("Details.cshtml");

        Assert.Contains("Tổng chi phí vật tư định mức", view);
        Assert.Contains("@Model.TotalMaterialCost.ToVietnameseNumber() VNĐ", view);
        Assert.Contains("Tổng chi phí vận hành định mức", view);
        Assert.Contains("@Model.TotalOperationCost.ToVietnameseNumber() VNĐ", view);
        Assert.Contains("TỔNG GIÁ THÀNH ĐỊNH MỨC TIÊU CHUẨN", view);
        Assert.Contains("@Model.TotalStandardCost.ToVietnameseNumber() VNĐ", view);
    }

    [Fact]
    public void Create_UsesIndexedAccessibleDynamicRowsAndAlwaysRetainsOne()
    {
        var view = ReadView("Create.cshtml");

        Assert.Contains("@model WmsMes.Web.ViewModels.BomCreateInputModel", view);
        Assert.Contains("asp-validation-summary=\"ModelOnly\"", view);
        Assert.Contains("asp-for=\"Items[index].ComponentProductId\"", view);
        Assert.Contains("asp-for=\"Items[index].QtyPer\"", view);
        Assert.Contains("asp-for=\"Items[index].ScrapPercent\"", view);
        Assert.Contains("type=\"button\"", view);
        Assert.Contains("aria-label=\"Xóa vật tư\"", view);
        Assert.Contains("function reindexItems()", view);
        Assert.Contains("Items[${index}]", view);
        Assert.Contains("rows().length === 1", view);
        Assert.Contains("table-responsive", view);
    }

    [Fact]
    public void Create_DynamicTemplateReindexesNamesIdsLabelsAndValidationTargets()
    {
        var view = ReadView("Create.cshtml");

        Assert.Contains("name=\"Items[__index__].ComponentProductId\"", view);
        Assert.Contains("name=\"Items[__index__].QtyPer\"", view);
        Assert.Contains("name=\"Items[__index__].ScrapPercent\"", view);
        Assert.Contains("querySelectorAll('[name]')", view);
        Assert.Contains("querySelectorAll('[id]')", view);
        Assert.Contains("querySelectorAll('label[for]')", view);
        Assert.Contains("querySelectorAll('[data-valmsg-for]')", view);
        Assert.Contains("data-val=\"true\"", view);
        Assert.Contains("data-val-range-min=\"0.0001\"", view);
        Assert.Contains("data-val-range-max=\"100\"", view);
    }

    private static string ReadView(string fileName) =>
        File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Views", "Bom", fileName));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WmsMes.sln")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}

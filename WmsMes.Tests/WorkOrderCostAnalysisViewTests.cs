namespace WmsMes.Tests;

public class WorkOrderCostAnalysisViewTests
{
    [Fact]
    public void Details_RendersAccessibleComparativeCostTableFromTypedAnalysisModel()
    {
        var view = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Views",
            "WorkOrder",
            "Details.cshtml"));

        Assert.Contains("@model WmsMes.Web.ViewModels.WorkOrderDetailsViewModel", view);
        Assert.Contains("Bảng Phân tích Giá thành Sản xuất", view);
        Assert.Contains("aria-labelledby=\"production-cost-analysis-heading\"", view);
        Assert.Contains("card-header bg-info text-dark", view);
        Assert.Contains("Khoản mục chi phí", view);
        Assert.Contains("Định mức (Target)", view);
        Assert.Contains("Thực tế (Actual)", view);
        Assert.Contains("Chênh lệch (Variance)", view);
        Assert.Contains("Chi phí vật tư", view);
        Assert.Contains("Chi phí nhân công", view);
        Assert.Contains("Chi phí vận hành máy", view);
        Assert.Contains("TỔNG CỘNG", view);
        Assert.Contains("Giá thành đơn vị", view);
        Assert.Contains("@Model.CostAnalysis.MaterialCost.Target.ToVietnameseNumber() VNĐ", view);
        Assert.Contains("@Model.CostAnalysis.MaterialCost.Actual.ToVietnameseNumber() VNĐ", view);
        Assert.Contains("@Model.CostAnalysis.MaterialCost.Variance.ToVietnameseNumber() VNĐ", view);
        Assert.Contains("@Model.CostAnalysis.LaborCost.Target.ToVietnameseNumber() VNĐ", view);
        Assert.Contains("@Model.CostAnalysis.MachineCost.Actual.ToVietnameseNumber() VNĐ", view);
        Assert.Contains("@Model.CostAnalysis.TotalCost.Variance.ToVietnameseNumber() VNĐ", view);
        Assert.Contains("@Model.CostAnalysis.UnitCost.Target.ToVietnameseNumber() VNĐ", view);
        Assert.Contains("@Model.CostAnalysis.UnitCost.Actual.ToVietnameseNumber() VNĐ", view);
        Assert.Contains("@Model.CostAnalysis.UnitCost.Variance.ToVietnameseNumber() VNĐ", view);
        Assert.DoesNotContain("_context", view);
        Assert.DoesNotContain("DbSet", view);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WmsMes.sln")))
            directory = directory.Parent;

        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }
}

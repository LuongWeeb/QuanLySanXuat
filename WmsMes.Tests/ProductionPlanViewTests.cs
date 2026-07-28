namespace WmsMes.Tests;

public class ProductionPlanViewTests
{
    [Fact]
    public void Index_ShowsResponsivePlanListAndEmptyState()
    {
        var view = ReadView("Index.cshtml");

        Assert.Contains("@model IEnumerable<WmsMes.Web.Domain.Entities.ProductionPlan>", view);
        Assert.Contains("table-responsive", view);
        Assert.Contains("Chưa có kế hoạch sản xuất", view);
        Assert.Contains("asp-action=\"Create\"", view);
        Assert.Contains("asp-action=\"Details\"", view);
    }

    [Fact]
    public void Create_UsesAccessibleDynamicProductRows()
    {
        var view = ReadView("Create.cshtml");

        Assert.Contains("asp-validation-summary=\"ModelOnly\"", view);
        Assert.Contains("name=\"productIds\"", view);
        Assert.Contains("name=\"plannedQtys\"", view);
        Assert.Contains("id=\"plan-items-body\"", view);
        Assert.Contains("type=\"button\"", view);
        Assert.Contains("aria-label=\"Xóa dòng sản phẩm\"", view);
        Assert.Contains("min=\"0.01\"", view);
        Assert.Contains("table-responsive", view);
    }

    [Fact]
    public void Details_ProvidesMrpWorkOrderAndCompletionActions()
    {
        var view = ReadView("Details.cshtml");

        Assert.Contains("asp-action=\"RunMrp\"", view);
        Assert.Contains("asp-action=\"GenerateWorkOrders\"", view);
        Assert.Contains("asp-action=\"Complete\"", view);
        Assert.Contains("Nhu cầu thô", view);
        Assert.Contains("Thiếu hụt", view);
        Assert.Contains("Lệnh sản xuất liên kết", view);
        Assert.Contains("table-responsive", view);
    }

    [Fact]
    public void Layout_ContainsProductionPlanNavigationForPlanningRoles()
    {
        var layout = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Views",
            "Shared",
            "_Layout.cshtml"));

        Assert.Contains("asp-controller=\"ProductionPlan\"", layout);
        Assert.Contains("Kế hoạch sản xuất (MRP)", layout);
    }

    private static string ReadView(string fileName) =>
        File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "Views",
            "ProductionPlan",
            fileName));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "WmsMes.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}

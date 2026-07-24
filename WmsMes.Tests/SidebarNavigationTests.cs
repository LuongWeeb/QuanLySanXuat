using System.Text.RegularExpressions;

namespace WmsMes.Tests;

public class SidebarNavigationTests
{
    [Fact]
    public void Layout_GroupsSidebarLinksByOperationalModule()
    {
        var layoutPath = Path.Combine(FindRepositoryRoot(), "Views", "Shared", "_Layout.cshtml");
        var layout = File.ReadAllText(layoutPath);

        AssertSection(layout, "Tổng quan");
        AssertSection(layout, "Quản lý Kho (WMS)");
        AssertSection(layout, "Quản lý Sản xuất (MES)");
        AssertSection(layout, "Kiểm soát Chất lượng & Truy vết");

        AssertLink(layout, "Home", "Index", "Dashboard");
        AssertLink(layout, "Inventory", "Index", "Số dư tồn kho");
        AssertLink(layout, "Inventory", "Receipts", "Nhập kho");
        AssertLink(layout, "Inventory", "Issues", "Xuất kho");
        AssertLink(layout, "Warehouse", "Index", "Kho & Vị trí");
        AssertLink(layout, "WorkOrder", "Index", "Lệnh sản xuất");
        AssertLink(layout, "Worker", "Index", "Trạm vận hành");
        AssertLink(layout, "Mrp", "Index", "Lập kế hoạch MRP");
        AssertLink(layout, "Product", "Index", "Sản phẩm (SKU)");
        AssertLink(layout, "Bom", "Index", "Định mức vật tư (BOM)");
        AssertLink(layout, "Qc", "Index", "Kiểm định chất lượng");
        AssertLink(layout, "Traceability", "Index", "Truy vết lô hàng");
    }

    [Fact]
    public void Layout_RestrictsProductionPlanningLinksToProductionManagementRoles()
    {
        var layoutPath = Path.Combine(FindRepositoryRoot(), "Views", "Shared", "_Layout.cshtml");
        var layout = File.ReadAllText(layoutPath);
        var roleBoundary = new Regex(
            """@if\s*\(User\.IsInRole\("Admin"\)\s*\|\|\s*User\.IsInRole\("Planner"\)\s*\|\|\s*User\.IsInRole\("Manager"\)\)\s*\{(?<links>[\s\S]*?)\}""",
            RegexOptions.IgnoreCase);

        var match = roleBoundary.Match(layout);
        Assert.True(match.Success, "Production-management role boundary was not found.");
        var links = match.Groups["links"].Value;
        AssertLink(links, "WorkOrder", "Index", "Lệnh sản xuất");
        AssertLink(links, "Mrp", "Index", "Lập kế hoạch MRP");
        AssertLink(links, "Product", "Index", "Sản phẩm (SKU)");
        AssertLink(links, "Bom", "Index", "Định mức vật tư (BOM)");
    }

    private static void AssertSection(string layout, string title)
    {
        var pattern = $"<div\\s+class=\"nav-section-title\"\\s*>\\s*{Regex.Escape(title)}\\s*</div>";
        Assert.Matches(new Regex(pattern, RegexOptions.IgnoreCase), layout);
    }

    private static void AssertLink(string layout, string controller, string action, string label)
    {
        var pattern = $"<a\\s+asp-controller=\"{Regex.Escape(controller)}\"\\s+asp-action=\"{Regex.Escape(action)}\"\\s*>\\s*{Regex.Escape(label)}\\s*</a>";
        Assert.Matches(new Regex(pattern, RegexOptions.IgnoreCase), layout);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WmsMes.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}

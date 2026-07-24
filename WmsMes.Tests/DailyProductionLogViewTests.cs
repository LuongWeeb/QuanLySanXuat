namespace WmsMes.Tests;

public class DailyProductionLogViewTests
{
    [Fact]
    public void Index_RendersSafeClampedProgressAndScheduleFeedback()
    {
        var view = ReadView("Index.cshtml");

        Assert.Contains("DailyProductionLogs.Sum", view);
        Assert.Contains("order.Qty > 0", view);
        Assert.Contains("Math.Clamp", view);
        Assert.Contains("CultureInfo.InvariantCulture", view);
        Assert.Contains("role=\"progressbar\"", view);
        Assert.Contains("aria-valuemin=\"0\"", view);
        Assert.Contains("aria-valuemax=\"100\"", view);
        Assert.Contains("Trễ hạn sản xuất!", view);
        Assert.Contains("Còn @daysRemaining ngày", view);
    }

    [Fact]
    public void Details_RendersProgressHistoryEmptyStateAndAccessibleQuickAddForm()
    {
        var view = ReadView("Details.cshtml");

        Assert.Contains("DailyProductionLogs.Sum", view);
        Assert.Contains("Math.Clamp", view);
        Assert.Contains("CultureInfo.InvariantCulture", view);
        Assert.Contains("Trễ hạn sản xuất!", view);
        Assert.Contains("Còn @daysRemaining ngày", view);
        Assert.Contains("Nhật ký sản lượng", view);
        Assert.Contains("table-responsive", view);
        Assert.Contains("OrderByDescending", view);
        Assert.Contains("Chưa có nhật ký sản lượng.", view);
        Assert.Contains("Model.Status == WorkOrderStatus.InProgress", view);
        Assert.Contains("asp-action=\"AddDailyLog\"", view);
        Assert.Contains("asp-validation-summary=\"All\"", view);
        Assert.Contains("for=\"daily-log-date\"", view);
        Assert.Contains("for=\"daily-log-quantity\"", view);
        Assert.Contains("for=\"daily-log-notes\"", view);
        Assert.Contains("name=\"Date\"", view);
        Assert.Contains("name=\"QtyProduced\"", view);
        Assert.Contains("name=\"Notes\"", view);
        Assert.Contains("type=\"date\"", view);
        Assert.Contains("maxlength=\"250\"", view);
    }

    private static string ReadView(string fileName) =>
        File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Views", "WorkOrder", fileName));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WmsMes.sln")))
            directory = directory.Parent;

        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }
}

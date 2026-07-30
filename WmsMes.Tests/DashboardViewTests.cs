namespace WmsMes.Tests;

public class DashboardViewTests
{
    [Fact]
    public void DashboardView_AndLayout_ExposeFactoryOeeRealtimeContract()
    {
        var dashboardPath = Path.Combine(ProjectRoot(), "Views", "Dashboard", "Index.cshtml");
        Assert.True(File.Exists(dashboardPath), $"Dashboard view was not found at {dashboardPath}.");

        var view = File.ReadAllText(dashboardPath);
        var layout = File.ReadAllText(
            Path.Combine(ProjectRoot(), "Views", "Shared", "_Layout.cshtml"));

        Assert.Contains("Dashboard Giám sát Nhà máy & Báo cáo OEE", view);
        Assert.Contains("id=\"oee-cards-container\"", view);
        Assert.Contains("/Dashboard/GetOeeData", view);
        Assert.Contains("/Dashboard/GetAgingData", view);
        Assert.Contains("/Dashboard/GetProductionProgressData", view);
        Assert.Contains(
            "https://cdn.jsdelivr.net/npm/chart.js@4.4.9/dist/chart.umd.min.js",
            view);
        Assert.Contains(
            "https://cdnjs.cloudflare.com/ajax/libs/microsoft-signalr/8.0.0/signalr.min.js",
            view);
        Assert.Contains("/productionHub", view);
        Assert.Contains("ReceiveProgressUpdate", view);
        Assert.DoesNotContain("location.reload", view);

        Assert.Contains("asp-controller=\"Dashboard\"", layout);
        Assert.Contains("asp-action=\"Index\"", layout);
        Assert.Contains("Dashboard Nhà máy & OEE", layout);
    }

    [Fact]
    public void DashboardView_ProvidesAccessibleResponsiveStatesAndSafeRendering()
    {
        var dashboardPath = Path.Combine(ProjectRoot(), "Views", "Dashboard", "Index.cshtml");
        Assert.True(File.Exists(dashboardPath), $"Dashboard view was not found at {dashboardPath}.");

        var view = File.ReadAllText(dashboardPath);

        Assert.Contains("aria-live=\"polite\"", view);
        Assert.Contains("role=\"status\"", view);
        Assert.Contains("setAttribute(\"role\", \"progressbar\")", view);
        Assert.Contains("aria-valuemin", view);
        Assert.Contains("aria-valuemax", view);
        Assert.Contains("textContent", view);
        Assert.DoesNotContain("innerHTML", view);
        Assert.Contains("Intl.NumberFormat(\"vi-VN\"", view);
        Assert.Contains("bg-success", view);
        Assert.Contains("bg-warning", view);
        Assert.Contains("bg-danger", view);
        Assert.Contains("chart.destroy()", view);
        Assert.Contains("Promise.all", view);
        Assert.Contains("Không có dữ liệu", view);
        Assert.Contains("Không thể tải dữ liệu", view);
    }

    [Fact]
    public void DashboardView_CoalescesRealtimeRefreshesWithoutAbortingInflightRequests()
    {
        var view = ReadDashboardView();

        Assert.Contains("let refreshInProgress = false", view);
        Assert.Contains("let refreshPending = false", view);
        Assert.Contains("if (refreshInProgress)", view);
        Assert.Contains("refreshPending = true", view);
        Assert.Contains("if (refreshPending)", view);
        Assert.Contains("if (refreshTimer) return", view);
        Assert.Contains("refreshTimer = undefined", view);
        Assert.DoesNotContain("AbortController", view);
        Assert.DoesNotContain("activeRequest?.abort()", view);
        Assert.DoesNotContain("window.clearTimeout(refreshTimer)", view);
    }

    [Fact]
    public void DashboardView_ProvidesSanitizedScreenReaderSummariesForBothCharts()
    {
        var view = ReadDashboardView();

        Assert.Contains("aria-describedby=\"production-chart-summary\"", view);
        Assert.Contains("id=\"production-chart-summary\"", view);
        Assert.Contains("id=\"production-chart-summary-body\"", view);
        Assert.Contains("aria-describedby=\"aging-chart-summary\"", view);
        Assert.Contains("id=\"aging-chart-summary\"", view);
        Assert.Contains("id=\"aging-chart-summary-body\"", view);
        Assert.Contains("renderProductionSummary(productionData)", view);
        Assert.Contains("renderAgingSummary(agingData)", view);
        Assert.Contains("replaceChildren()", view);
        Assert.Contains("textContent", view);
        Assert.DoesNotContain("innerHTML", view);
    }

    [Fact]
    public void DashboardView_UsesAccessibleWarningContrastAndMetricLabels()
    {
        var view = ReadDashboardView();

        Assert.Contains("#7a4b00", view);
        Assert.Contains("metric.setAttribute(\"role\", \"group\")", view);
        Assert.Contains("metric.setAttribute(\"aria-label\"", view);
        Assert.Contains("Mức độ sẵn sàng", view);
        Assert.Contains("Hiệu suất", view);
        Assert.Contains("Chất lượng", view);
    }

    [Fact]
    public void DashboardView_ShowsAVisibleFallbackWhenChartJsIsUnavailable()
    {
        var view = ReadDashboardView();

        Assert.Contains("id=\"chart-error\"", view);
        Assert.Contains("typeof window.Chart !== \"function\"", view);
        Assert.Contains("Không thể hiển thị biểu đồ", view);
    }

    private static string ReadDashboardView() =>
        File.ReadAllText(Path.Combine(ProjectRoot(), "Views", "Dashboard", "Index.cshtml"));

    private static string ProjectRoot() => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
}

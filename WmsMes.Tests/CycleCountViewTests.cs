namespace WmsMes.Tests;

public class CycleCountViewTests
{
    [Fact]
    public void ExecuteScan_ProvidesKeyboardAndCameraBarcodeScanning()
    {
        var view = ReadView("ExecuteScan.cshtml");
        var script = File.ReadAllText(Path.Combine(
            ProjectRoot(),
            "wwwroot",
            "js",
            "cycle-count-scan.js"));

        Assert.Contains("name=\"itemCounts[", view);
        Assert.Contains("id=\"barcode-input\"", view);
        Assert.Contains("id=\"start-camera\"", view);
        Assert.Contains("BarcodeDetector", script);
        Assert.Contains("getUserMedia", script);
        Assert.Contains("data-location-code", view);
        Assert.Contains("data-lot-no", view);
        Assert.DoesNotContain("CountedQty?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? \"0\"", view);
        Assert.Contains("selectedLocation", script);
    }

    [Fact]
    public void ExecuteScan_ProvidesAccessibleReasonForEveryCountedItem()
    {
        var view = ReadView("ExecuteScan.cshtml");

        Assert.Contains("<th>Lý do chênh lệch</th>", view);
        Assert.Contains("for=\"reason-@item.Id\"", view);
        Assert.Contains("id=\"reason-@item.Id\"", view);
        Assert.Contains("name=\"itemReasons[@item.Id]\"", view);
        Assert.Contains("maxlength=\"250\"", view);
        Assert.Contains("@item.ReasonNote", view);
    }

    [Fact]
    public void Details_ShowsFinancialVarianceAndManagerApproval()
    {
        var view = ReadView("Details.cshtml");

        Assert.Contains("Tổng giá trị chênh lệch", view);
        Assert.Contains("VarianceQty", view);
        Assert.Contains("User.IsInRole(\"Manager\")", view);
        Assert.Contains("asp-action=\"Approve\"", view);
    }

    [Fact]
    public void Details_ShowsReasonAndCycleCountPrintLink()
    {
        var view = ReadView("Details.cshtml");

        Assert.Contains("<th>Lý do chênh lệch</th>", view);
        Assert.Contains("@item.ReasonNote", view);
        Assert.Contains("href=\"/api/print/cyclecount/@Model.Id\"", view);
        Assert.Contains("target=\"_blank\"", view);
    }

    private static string ReadView(string name) =>
        File.ReadAllText(Path.Combine(
            ProjectRoot(),
            "Views",
            "CycleCount",
            name));

    private static string ProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "WmsMes.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ??
            throw new DirectoryNotFoundException("Project root not found.");
    }
}

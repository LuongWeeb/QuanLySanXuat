using System.Text.RegularExpressions;

namespace WmsMes.Tests;

public class WorkCenterCostViewTests
{
    [Theory]
    [InlineData("Create.cshtml")]
    [InlineData("Edit.cshtml")]
    public void RateForm_ProvidesRequiredTwoDecimalCostInputs(string fileName)
    {
        var path = Path.Combine(
            FindRepositoryRoot(), "Views", "WorkCenter", fileName);

        Assert.True(File.Exists(path), $"Missing WorkCenter rate form: {fileName}");
        var view = File.ReadAllText(path);

        AssertRateInput(view, "HourlyLaborRate", "Chi phí nhân công mỗi giờ (VNĐ)");
        AssertRateInput(view, "HourlyMachineRate", "Chi phí máy móc mỗi giờ (VNĐ)");
    }

    private static void AssertRateInput(string view, string propertyName, string label)
    {
        Assert.Contains(label, view);
        Assert.Matches(
            new Regex(
                $"<input(?=[^>]*asp-for=\\\"{propertyName}\\\")(?=[^>]*type=\\\"number\\\")(?=[^>]*min=\\\"0\\\")(?=[^>]*step=\\\"0\\.01\\\")(?=[^>]*required)[^>]*>",
                RegexOptions.IgnoreCase),
            view);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WmsMes.sln")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}

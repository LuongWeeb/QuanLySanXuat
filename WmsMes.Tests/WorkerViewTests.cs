namespace WmsMes.Tests;

public class WorkerViewTests
{
    [Fact]
    public void WorkerStation_ProvidesStateAwareWorkOrderScanning()
    {
        var view = File.ReadAllText(Path.Combine(
            ProjectRoot(),
            "Views",
            "Worker",
            "Index.cshtml"));

        Assert.Contains("id=\"worker-scanner-input\"", view);
        Assert.Contains("aria-live=\"polite\"", view);
        Assert.Contains("data-work-order-code=", view);
        Assert.Contains("data-step-status=", view);
        Assert.Contains("processWorkerScan", view);
        Assert.Contains("event.key === 'Enter'", view);
        Assert.Contains("WorkOrderStepStatus.Pending.ToString()", view);
        Assert.Contains("WorkOrderStepStatus.InProgress.ToString()", view);
        Assert.Contains("form[action*=\"Start\"]", view);
        Assert.Contains("input[name=\"qtyOk\"]", view);
        Assert.Contains("scrollIntoView", view);
    }

    private static string ProjectRoot() => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
}

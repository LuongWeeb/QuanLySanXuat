using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using WmsMes.Web.Controllers;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Domain.Enums;
using WmsMes.Web.Services;

namespace WmsMes.Tests;

public class WorkerControllerTests
{
    [Fact]
    public async Task Index_ReturnsOnlyUnfinishedStepsForApprovedOrInProgressWorkOrders()
    {
        await using var context = new ApplicationDbContext(Options());
        context.WorkOrders.AddRange(
            OrderWithStep("WO-APPROVED", WorkOrderStatus.Approved, WorkOrderStepStatus.Pending),
            OrderWithStep("WO-IN-PROGRESS", WorkOrderStatus.InProgress, WorkOrderStepStatus.InProgress),
            OrderWithStep("WO-DRAFT", WorkOrderStatus.Draft, WorkOrderStepStatus.Pending),
            OrderWithStep("WO-PENDING", WorkOrderStatus.Pending, WorkOrderStepStatus.Pending),
            OrderWithStep("WO-COMPLETED", WorkOrderStatus.Completed, WorkOrderStepStatus.Pending),
            OrderWithStep("WO-FINISHED-STEP", WorkOrderStatus.Approved, WorkOrderStepStatus.Completed));
        await context.SaveChangesAsync();

        var result = await Controller(context).Index();

        var steps = Assert.IsAssignableFrom<IEnumerable<WorkOrderStep>>(Assert.IsType<ViewResult>(result).Model).ToList();
        Assert.Equal(new[] { "WO-APPROVED", "WO-IN-PROGRESS" }, steps.Select(step => step.WorkOrder!.Code));
    }

    private static DbContextOptions<ApplicationDbContext> Options() =>
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase($"Worker_{Guid.NewGuid()}").Options;

    private static WorkerController Controller(ApplicationDbContext context) =>
        new(context, Mock.Of<IWorkOrderService>());

    private static WorkOrder OrderWithStep(string code, WorkOrderStatus status, WorkOrderStepStatus stepStatus)
    {
        var product = new Product { Code = $"P-{code}", Name = code, IsActive = true, IsManufactured = true };
        var order = new WorkOrder
        {
            Code = code,
            Product = product,
            Qty = 10,
            DueDate = new DateTime(2026, 7, 22),
            Status = status,
            BomVersion = "B1",
            RoutingVersion = "R1"
        };
        order.Steps.Add(new WorkOrderStep
        {
            StepNumber = 1,
            StepName = "Process",
            Status = stepStatus,
            WorkCenter = new WorkCenter { Code = $"WC-{code}", Name = code }
        });
        return order;
    }
}

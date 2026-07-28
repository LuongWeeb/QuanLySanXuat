using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Moq;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Services;

namespace WmsMes.Tests;

public class BuyingSellingControllerTests
{
    [Theory]
    [InlineData("WmsMes.Web.Controllers.SalesOrderController")]
    [InlineData("WmsMes.Web.Controllers.PurchaseOrderController")]
    public void BuyingAndSellingControllers_RestrictAccessToBusinessRoles(string typeName)
    {
        var controllerType = ControllerType(typeName);

        var authorize = Assert.Single(
            controllerType.GetCustomAttributes<AuthorizeAttribute>());

        Assert.Equal("Admin,Manager,Planner", authorize.Roles);
    }

    [Fact]
    public async Task GeneratePurchaseRequest_CreatesRequestAndRedirectsToPlan()
    {
        await using var context = CreateContext();
        var planService = new Mock<IProductionPlanService>();
        var requestService = new Mock<IPurchaseRequestService>();
        requestService
            .Setup(service => service.GenerateFromMrpAsync(7, "planner-1"))
            .ReturnsAsync(new PurchaseRequest { Id = 3, RequestNo = "PR-PP-007" });
        var controllerType = ControllerType(
            "WmsMes.Web.Controllers.ProductionPlanController");
        var controller = Assert.IsAssignableFrom<Controller>(
            Activator.CreateInstance(
                controllerType,
                context,
                planService.Object,
                requestService.Object));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "planner-1")
                ]))
            }
        };
        controller.TempData = new TempDataDictionary(
            controller.HttpContext,
            Mock.Of<ITempDataProvider>());
        var action = controllerType.GetMethod(
            "GeneratePurchaseRequest",
            [typeof(int)]);
        Assert.NotNull(action);

        var task = Assert.IsAssignableFrom<Task<IActionResult>>(
            action!.Invoke(controller, [7]));
        var redirect = Assert.IsType<RedirectToActionResult>(await task);

        Assert.Equal("Details", redirect.ActionName);
        Assert.Equal(7, redirect.RouteValues!["id"]);
        Assert.Contains("PR-PP-007", controller.TempData["StatusMessage"]!.ToString());
        requestService.VerifyAll();
    }

    private static Type ControllerType(string typeName)
    {
        var type = typeof(ApplicationDbContext).Assembly.GetType(typeName);
        Assert.NotNull(type);
        return type!;
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }
}

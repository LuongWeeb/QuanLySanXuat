using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Moq;
using WmsMes.Web.Controllers;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Services;

namespace WmsMes.Tests;

public class CycleCountControllerTests
{
    [Fact]
    public void ControllerAndPosts_RequireExpectedRolesAndAntiforgery()
    {
        var authorize = Assert.Single(
            typeof(CycleCountController)
                .GetCustomAttributes<AuthorizeAttribute>());
        Assert.Equal("Admin,Warehouse,Manager", authorize.Roles);

        foreach (var action in new[] { "Create", "SaveScan", "AddDiscoveredItem", "Approve" })
        {
            var method = typeof(CycleCountController)
                .GetMethods()
                .Single(candidate =>
                    candidate.Name == action &&
                    candidate.GetCustomAttribute<HttpPostAttribute>() is not null);
            Assert.NotNull(method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        }

        var approve = typeof(CycleCountController)
            .GetMethods()
            .Single(method =>
                method.Name == "Approve" &&
                method.GetCustomAttribute<HttpPostAttribute>() is not null);
        Assert.Equal(
            "Admin,Manager",
            Assert.Single(approve.GetCustomAttributes<AuthorizeAttribute>()).Roles);
    }

    [Fact]
    public async Task Create_ValidWarehouseUsesAuthenticatedUserAndRedirectsToScan()
    {
        await using var context = CreateContext();
        context.Warehouses.Add(new Warehouse
        {
            Id = 1,
            Code = "WH-01",
            Name = "Kho chính"
        });
        await context.SaveChangesAsync();
        var service = new Mock<ICycleCountService>();
        service.Setup(item => item.CreateOrderAsync(1, "user-1"))
            .ReturnsAsync(new CycleCountOrder { Id = 7 });
        var controller = CreateController(context, service.Object, "user-1");

        var result = Assert.IsType<RedirectToActionResult>(
            await controller.Create(1));

        Assert.Equal(nameof(CycleCountController.ExecuteScan), result.ActionName);
        Assert.Equal(7, result.RouteValues!["id"]);
        service.VerifyAll();
    }

    [Fact]
    public async Task Create_InvalidWarehouseReturnsViewWithoutCallingService()
    {
        await using var context = CreateContext();
        var service = new Mock<ICycleCountService>();
        var controller = CreateController(context, service.Object, "user-1");

        Assert.IsType<ViewResult>(await controller.Create(999));

        Assert.False(controller.ModelState.IsValid);
        service.VerifyNoOtherCalls();
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static CycleCountController CreateController(
        ApplicationDbContext context,
        ICycleCountService service,
        string userId)
    {
        var controller = new CycleCountController(context, service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, userId)],
                        "test"))
                }
            }
        };
        controller.TempData = new TempDataDictionary(
            controller.HttpContext,
            Mock.Of<ITempDataProvider>());
        return controller;
    }
}

using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using WmsMes.Web.Controllers;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Services;

namespace WmsMes.Tests;

public class NotificationLifecycleTests
{
    [Fact]
    public async Task MarkAllAsReadAsync_MarksUnreadNotificationsAndReturnsUpdatedCount()
    {
        await using var context = CreateContext();
        context.AppNotifications.AddRange(
            new AppNotification { Id = 1, Title = "Unread 1", Message = "Message", IsRead = false },
            new AppNotification { Id = 2, Title = "Read", Message = "Message", IsRead = true },
            new AppNotification { Id = 3, Title = "Unread 2", Message = "Message", IsRead = false });
        await context.SaveChangesAsync();

        var updated = await new NotificationService(context).MarkAllAsReadAsync();

        Assert.Equal(2, updated);
        Assert.Equal(0, await new NotificationService(context).GetUnreadCountAsync());
        Assert.All(await context.AppNotifications.ToListAsync(), notification => Assert.True(notification.IsRead));
    }

    [Fact]
    public async Task MarkAllAsRead_PostUsesServiceAndRedirectsToHome()
    {
        var service = new Mock<INotificationService>();
        service.Setup(candidate => candidate.MarkAllAsReadAsync()).ReturnsAsync(3);
        var controller = new NotificationController(service.Object);

        var result = await controller.MarkAllAsRead();

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Home", redirect.ControllerName);
        service.Verify(candidate => candidate.MarkAllAsReadAsync(), Times.Once);
    }

    [Fact]
    public void MarkAllAsRead_PostRequiresBusinessRoleAuthorizationAndAntiforgery()
    {
        var authorize = Assert.Single(
            typeof(NotificationController).GetCustomAttributes<AuthorizeAttribute>());
        Assert.Equal("Admin,Warehouse,Manager", authorize.Roles);
        var action = typeof(NotificationController).GetMethod(
            nameof(NotificationController.MarkAllAsRead));

        Assert.NotNull(action);
        Assert.Single(action!.GetCustomAttributes<HttpPostAttribute>());
        Assert.Single(action.GetCustomAttributes<ValidateAntiForgeryTokenAttribute>());
    }

    [Fact]
    public void LayoutOffersSecureMarkAllReadActionWithoutHardcodedBadgeMutation()
    {
        var layout = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "Views", "Shared", "_Layout.cshtml"));

        Assert.Contains("asp-controller=\"Notification\"", layout, StringComparison.Ordinal);
        Assert.Contains("asp-action=\"MarkAllAsRead\"", layout, StringComparison.Ordinal);
        Assert.Contains("method=\"post\"", layout, StringComparison.Ordinal);
        Assert.Contains("id=\"mark-all-notifications-form\"", layout, StringComparison.Ordinal);
        Assert.Contains("id=\"mark-all-notifications-button\"", layout, StringComparison.Ordinal);
        Assert.Contains("markAllForm.classList.toggle(\"d-none\", count === 0)", layout, StringComparison.Ordinal);
        Assert.Contains("markAllButton.disabled = count === 0", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("updateBadge(-", layout, StringComparison.Ordinal);
    }

    private static ApplicationDbContext CreateContext() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}

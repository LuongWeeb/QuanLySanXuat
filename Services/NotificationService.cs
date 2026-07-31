using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Hubs;

namespace WmsMes.Web.Services;

public class NotificationService : INotificationService
{
    private readonly ApplicationDbContext _context;
    private readonly IHubContext<NotificationHub>? _notificationHub;

    public NotificationService(
        ApplicationDbContext context,
        IHubContext<NotificationHub>? notificationHub = null)
    {
        _context = context;
        _notificationHub = notificationHub;
    }

    public async Task SendNotificationAsync(
        string title,
        string message,
        string severity,
        string? referenceUrl = null)
    {
        var notification = new AppNotification
        {
            Title = title,
            Message = message,
            Severity = severity,
            CreatedAt = DateTime.UtcNow,
            ReferenceUrl = referenceUrl
        };
        _context.AppNotifications.Add(notification);
        await _context.SaveChangesAsync();

        if (_notificationHub is not null)
        {
            await _notificationHub.Clients.All.SendAsync("ReceiveNotification", notification);
        }
    }

    public Task<int> GetUnreadCountAsync()
    {
        return _context.AppNotifications.CountAsync(notification => !notification.IsRead);
    }

    public async Task<IEnumerable<AppNotification>> GetRecentNotificationsAsync(int take = 5)
    {
        return await _context.AppNotifications
            .AsNoTracking()
            .OrderByDescending(notification => notification.CreatedAt)
            .Take(take)
            .ToListAsync();
    }
}

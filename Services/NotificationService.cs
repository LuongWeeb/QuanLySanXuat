using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WmsMes.Web.Data;
using WmsMes.Web.Domain.Entities;
using WmsMes.Web.Hubs;

namespace WmsMes.Web.Services;

public class NotificationService : INotificationService
{
    private readonly ApplicationDbContext _context;
    private readonly IHubContext<NotificationHub>? _notificationHub;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        ApplicationDbContext context,
        IHubContext<NotificationHub>? notificationHub = null,
        ILogger<NotificationService>? logger = null)
    {
        _context = context;
        _notificationHub = notificationHub;
        _logger = logger ?? NullLogger<NotificationService>.Instance;
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
            try
            {
                await _notificationHub.Clients.All.SendAsync("ReceiveNotification", notification);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Notification {NotificationId} persisted but realtime broadcast failed.",
                    notification.Id);
            }
        }
    }

    public Task<int> GetUnreadCountAsync()
    {
        return _context.AppNotifications.CountAsync(notification => !notification.IsRead);
    }

    public async Task<int> MarkAllAsReadAsync()
    {
        if (_context.Database.IsRelational())
        {
            return await _context.AppNotifications
                .Where(notification => !notification.IsRead)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(notification => notification.IsRead, true));
        }

        var unreadNotifications = await _context.AppNotifications
            .Where(notification => !notification.IsRead)
            .ToListAsync();
        foreach (var notification in unreadNotifications)
        {
            notification.IsRead = true;
        }

        await _context.SaveChangesAsync();
        return unreadNotifications.Count;
    }

    public async Task<IEnumerable<AppNotification>> GetRecentNotificationsAsync(int take = 5)
    {
        return await _context.AppNotifications
            .AsNoTracking()
            .OrderByDescending(notification => notification.CreatedAt)
            .ThenByDescending(notification => notification.Id)
            .Take(take)
            .ToListAsync();
    }
}

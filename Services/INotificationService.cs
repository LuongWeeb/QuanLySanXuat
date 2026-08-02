using WmsMes.Web.Domain.Entities;

namespace WmsMes.Web.Services;

public interface INotificationService
{
    Task SendNotificationAsync(string title, string message, string severity, string? referenceUrl = null);

    Task<int> GetUnreadCountAsync();

    Task<int> MarkAllAsReadAsync();

    Task<IEnumerable<AppNotification>> GetRecentNotificationsAsync(int take = 5);
}

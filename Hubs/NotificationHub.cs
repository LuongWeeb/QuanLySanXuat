using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace WmsMes.Web.Hubs;

[Authorize(Roles = "Admin,Warehouse,Manager")]
public class NotificationHub : Hub
{
}

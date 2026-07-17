using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace WmsMes.Web.Hubs;

[Authorize]
public class ProductionHub : Hub
{
}

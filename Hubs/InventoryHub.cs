using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace WmsMes.Web.Hubs;

[Authorize]
public class InventoryHub : Hub
{
    public async Task NotifyStockChange()
    {
        await Clients.All.SendAsync("ReceiveStockUpdate");
    }
}

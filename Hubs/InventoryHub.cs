using Microsoft.AspNetCore.SignalR;

namespace WmsMes.Web.Hubs;

public class InventoryHub : Hub
{
    public async Task NotifyStockChange()
    {
        await Clients.All.SendAsync("ReceiveStockUpdate");
    }
}

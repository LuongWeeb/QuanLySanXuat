using Microsoft.AspNetCore.SignalR;

namespace WmsMes.Web.Hubs;

public class ProductionHub : Hub
{
    public async Task NotifyProgressChange()
    {
        await Clients.All.SendAsync("ReceiveProgressUpdate");
    }
}

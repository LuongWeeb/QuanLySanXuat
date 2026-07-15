using Microsoft.AspNetCore.SignalR;

namespace WmsMes.Web.Hubs;

public class QualityHub : Hub
{
    public async Task SendQcAlert(string lotNo, string result)
    {
        await Clients.All.SendAsync("ReceiveQcAlert", lotNo, result);
    }
}

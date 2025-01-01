using Microsoft.AspNetCore.SignalR;
using nump.Components.Classes;

namespace nump.Components.Hubs;
public class TestHub : Hub
{
    public async Task SendMessage(string user, TaskProcess task)
    {
        await Clients.All.SendAsync("ReceiveMessage", user, task);
    }

}
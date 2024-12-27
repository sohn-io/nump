using Microsoft.AspNetCore.SignalR;
using nump.Components.Classes;

namespace nump.Components.Hubs;
public class TestHub : Hub
{
    public async Task SendMessage(string user, NumpInstructionSet task)
    {
        await Clients.All.SendAsync("ReceiveMessage", user, task);
    }

}
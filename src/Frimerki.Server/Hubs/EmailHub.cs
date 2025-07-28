using Microsoft.AspNetCore.SignalR;

namespace Frimerki.Server;

public class EmailHub : Hub
{
    public async Task JoinFolder(string folderId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"folder_{folderId}");
    }

    public async Task LeaveFolder(string folderId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"folder_{folderId}");
    }

    public override async Task OnConnectedAsync()
    {
        // You can add authentication logic here later
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await base.OnDisconnectedAsync(exception);
    }
}

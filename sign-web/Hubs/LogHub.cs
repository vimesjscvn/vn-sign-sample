using Microsoft.AspNetCore.SignalR;

namespace VMSign.Web.Hubs;

/// <summary>
/// SignalR hub for pushing real-time log messages to the client.
/// </summary>
public class LogHub : Hub
{
    public async Task SendLog(string level, string message)
    {
        await Clients.Caller.SendAsync("ReceiveLog", level, message, DateTime.Now.ToString("HH:mm:ss"));
    }
}

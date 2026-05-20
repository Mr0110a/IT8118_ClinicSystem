using Microsoft.AspNetCore.SignalR;

namespace ClinicMVC.Hubs
{
    /// <summary>
    /// SignalR hub that pushes new notifications to a specific user in real time.
    ///
    /// Each authenticated connection automatically joins a group named after
    /// their UserId, so the server can target notifications to that user only.
    /// </summary>
    public class NotificationHub : Hub
    {
        public override async Task OnConnectedAsync()
        {
            var userId = Context.UserIdentifier;
            if (!string.IsNullOrEmpty(userId))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, $"user-{userId}");
            }
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = Context.UserIdentifier;
            if (!string.IsNullOrEmpty(userId))
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user-{userId}");
            }
            await base.OnDisconnectedAsync(exception);
        }
    }
}

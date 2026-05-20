using Microsoft.AspNetCore.SignalR;

namespace ClinicMVC.Hubs
{
    /// <summary>
    /// SignalR hub that pushes live appointment status changes to any client
    /// viewing the live tracking board (waiting-room screen / receptionist view).
    ///
    /// Clients subscribe to the "live-board" group when they open the live
    /// board view; the server broadcasts updates to that group whenever an
    /// appointment status changes.
    /// </summary>
    public class AppointmentHub : Hub
    {
        public const string LiveBoardGroup = "live-board";

        /// <summary>Called by the client when joining the live board.</summary>
        public async Task JoinLiveBoard()
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, LiveBoardGroup);
        }

        /// <summary>Called when leaving the live board.</summary>
        public async Task LeaveLiveBoard()
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, LiveBoardGroup);
        }
    }
}

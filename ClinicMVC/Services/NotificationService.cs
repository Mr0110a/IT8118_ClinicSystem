using ClinicAPI.Data;
using ClinicAPI.Models.Domain;
using ClinicMVC.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace ClinicMVC.Services
{
    public interface INotificationService
    {
        Task CreateAsync(string userId, string message, int? appointmentId = null, int? patientId = null);
    }

    /// <summary>
    /// Persists a Notification row and immediately pushes it to the user via
    /// SignalR so the bell in the layout updates in real time.
    /// </summary>
    public class NotificationService : INotificationService
    {
        private readonly ClinicDbContext _db;
        private readonly IHubContext<NotificationHub> _hub;

        public NotificationService(ClinicDbContext db, IHubContext<NotificationHub> hub)
        {
            _db  = db;
            _hub = hub;
        }

        public async Task CreateAsync(string userId, string message, int? appointmentId = null, int? patientId = null)
        {
            if (string.IsNullOrWhiteSpace(userId)) return;

            var notification = new Notification
            {
                UserId               = userId,
                Message              = message,
                IsRead               = false,
                CreatedAt            = DateTime.UtcNow,
                RelatedAppointmentId = appointmentId,
                RelatedPatientId     = patientId
            };

            _db.Notifications.Add(notification);
            await _db.SaveChangesAsync();

            // Push to the user's group so their bell increments live
            await _hub.Clients.Group($"user-{userId}").SendAsync("NewNotification", new
            {
                notification.Id,
                notification.Message,
                CreatedAt = notification.CreatedAt
            });
        }
    }
}

using ClinicAPI.Data;
using ClinicAPI.Models.Domain;
using ClinicMVC.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ClinicMVC.Services
{
    /// <summary>
    /// Centralises all appointment business rules so controllers stay thin:
    /// - validates status transitions
    /// - creates VisitRecord on completion
    /// - emits in-system notifications to both patient & doctor
    /// - broadcasts to the SignalR live board
    /// </summary>
    public class AppointmentLifecycleService
    {
        private readonly ClinicDbContext _db;
        private readonly INotificationService _notifications;
        private readonly IHubContext<AppointmentHub> _hub;

        public AppointmentLifecycleService(
            ClinicDbContext db,
            INotificationService notifications,
            IHubContext<AppointmentHub> hub)
        {
            _db            = db;
            _notifications = notifications;
            _hub           = hub;
        }

        /// <summary>Whether a transition from current → next is allowed.</summary>
        public static bool IsValidTransition(AppointmentStatus current, AppointmentStatus next) =>
            (current, next) switch
            {
                (AppointmentStatus.Requested,  AppointmentStatus.Confirmed)  => true,
                (AppointmentStatus.Confirmed,  AppointmentStatus.CheckedIn)  => true,
                (AppointmentStatus.CheckedIn,  AppointmentStatus.InProgress) => true,
                (AppointmentStatus.InProgress, AppointmentStatus.Completed)  => true,
                // Cancel/Miss allowed from any active state (not from terminal states)
                (AppointmentStatus.Requested,  AppointmentStatus.Cancelled)  => true,
                (AppointmentStatus.Confirmed,  AppointmentStatus.Cancelled)  => true,
                (AppointmentStatus.CheckedIn,  AppointmentStatus.Cancelled)  => true,
                (AppointmentStatus.Requested,  AppointmentStatus.Missed)     => true,
                (AppointmentStatus.Confirmed,  AppointmentStatus.Missed)     => true,
                _ => false
            };

        /// <summary>List of legal next statuses for the given current status.</summary>
        public static IEnumerable<AppointmentStatus> AllowedTransitions(AppointmentStatus current) =>
            Enum.GetValues<AppointmentStatus>().Where(s => IsValidTransition(current, s));

        /// <summary>
        /// Apply a status transition, run any side-effects, persist, notify, and broadcast.
        /// Returns (success, errorMessage).
        /// </summary>
        public async Task<(bool ok, string? error)> ChangeStatusAsync(
            int appointmentId,
            AppointmentStatus newStatus,
            string? notes = null)
        {
            var appointment = await _db.Appointments
                .Include(a => a.Patient).ThenInclude(p => p!.User)
                .Include(a => a.Doctor).ThenInclude(d => d!.User)
                .FirstOrDefaultAsync(a => a.Id == appointmentId);

            if (appointment == null)
                return (false, "Appointment not found.");

            if (!IsValidTransition(appointment.Status, newStatus))
                return (false, $"Cannot move appointment from {appointment.Status} to {newStatus}.");

            var oldStatus = appointment.Status;
            appointment.Status = newStatus;
            if (!string.IsNullOrWhiteSpace(notes))
                appointment.Notes = notes;

            // Side-effect: when a doctor completes an appointment, automatically
            // create the empty visit record so the doctor can fill diagnosis next.
            if (newStatus == AppointmentStatus.Completed)
            {
                var hasVisit = await _db.VisitRecords.AnyAsync(v => v.AppointmentId == appointmentId);
                if (!hasVisit)
                {
                    _db.VisitRecords.Add(new VisitRecord
                    {
                        AppointmentId = appointmentId,
                        CreatedAt     = DateTime.UtcNow
                    });
                }
            }

            await _db.SaveChangesAsync();

            // Notify patient + doctor
            var patientUserId = appointment.Patient?.UserId;
            var doctorUserId  = appointment.Doctor?.UserId;
            var message       = $"Appointment #{appointment.ReferenceCode} is now {newStatus}.";

            if (!string.IsNullOrEmpty(patientUserId))
                await _notifications.CreateAsync(patientUserId, message, appointmentId, appointment.PatientId);

            if (!string.IsNullOrEmpty(doctorUserId))
                await _notifications.CreateAsync(doctorUserId, message, appointmentId);

            // Broadcast to the live board
            await _hub.Clients.Group(AppointmentHub.LiveBoardGroup).SendAsync("StatusChanged", new
            {
                appointment.Id,
                appointment.ReferenceCode,
                PatientName = appointment.Patient?.FullName,
                DoctorName  = appointment.Doctor?.FullName,
                StartTime   = appointment.DateTimeStart,
                OldStatus   = oldStatus.ToString(),
                NewStatus   = newStatus.ToString()
            });

            return (true, null);
        }
    }
}

using System.Security.Claims;
using ClinicAPI.Data;
using ClinicAPI.Models.Domain;
using ClinicMVC.Models;
using ClinicMVC.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicMVC.Controllers
{
    [Authorize]
    public class AppointmentsController : Controller
    {
        private readonly ClinicDbContext _db;
        private readonly AppointmentLifecycleService _lifecycle;
        private readonly INotificationService _notifications;

        public AppointmentsController(
            ClinicDbContext db,
            AppointmentLifecycleService lifecycle,
            INotificationService notifications)
        {
            _db            = db;
            _lifecycle     = lifecycle;
            _notifications = notifications;
        }

        // ─── List (role-aware) ────────────────────────────────────────────────
        public async Task<IActionResult> Index(string? status = null, int page = 1)
        {
            const int pageSize = 10;
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            IQueryable<Appointment> q = _db.Appointments
                .Include(a => a.Patient)
                .Include(a => a.Doctor);

            if (User.IsInRole("Patient"))
            {
                var patient = await _db.Patients.FirstOrDefaultAsync(p => p.UserId == userId);
                q = patient == null ? q.Where(_ => false)
                                    : q.Where(a => a.PatientId == patient.Id);
            }
            else if (User.IsInRole("Doctor"))
            {
                var doctor = await _db.Doctors.FirstOrDefaultAsync(d => d.UserId == userId);
                q = doctor == null ? q.Where(_ => false)
                                   : q.Where(a => a.DoctorId == doctor.Id);
            }
            // Receptionist / ClinicManager see everything

            if (!string.IsNullOrEmpty(status) && Enum.TryParse<AppointmentStatus>(status, out var s))
                q = q.Where(a => a.Status == s);

            var total = await q.CountAsync();
            var list = await q.OrderByDescending(a => a.DateTimeStart)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .ToListAsync();

            ViewBag.Page         = page;
            ViewBag.TotalPages   = (int)Math.Ceiling(total / (double)pageSize);
            ViewBag.StatusFilter = status;
            ViewBag.Statuses     = Enum.GetValues<AppointmentStatus>();
            return View(list);
        }

        public async Task<IActionResult> Details(int id)
        {
            var appt = await _db.Appointments
                .Include(a => a.Patient)
                .Include(a => a.Doctor).ThenInclude(d => d!.Specializations).ThenInclude(s => s.Specialization)
                .Include(a => a.VisitRecord)!.ThenInclude(v => v!.Prescriptions)
                .FirstOrDefaultAsync(a => a.Id == id);

            if (appt == null) return NotFound();
            if (!await CanViewAppointmentAsync(appt)) return Forbid();

            ViewBag.AllowedTransitions = AppointmentLifecycleService.AllowedTransitions(appt.Status);
            return View(appt);
        }

        // ─── Booking ──────────────────────────────────────────────────────────
        [HttpGet]
        [Authorize(Roles = "Patient,Receptionist")]
        public async Task<IActionResult> Book()
        {
            ViewBag.Specializations = await _db.Specializations.OrderBy(s => s.Name).ToListAsync();
            if (User.IsInRole("Receptionist"))
                ViewBag.Patients = await _db.Patients.OrderBy(p => p.FullName).ToListAsync();
            return View(new BookAppointmentViewModel());
        }

        [HttpPost, ValidateAntiForgeryToken]
        [Authorize(Roles = "Patient,Receptionist")]
        public async Task<IActionResult> Book(BookAppointmentViewModel model)
        {
            ViewBag.Specializations = await _db.Specializations.OrderBy(s => s.Name).ToListAsync();
            if (User.IsInRole("Receptionist"))
                ViewBag.Patients = await _db.Patients.OrderBy(p => p.FullName).ToListAsync();

            if (model.DateTimeEnd <= model.DateTimeStart)
                ModelState.AddModelError(nameof(model.DateTimeEnd), "End time must be after start time.");
            if (model.DateTimeStart < DateTime.Now.AddMinutes(-1))
                ModelState.AddModelError(nameof(model.DateTimeStart), "Cannot book a slot in the past.");

            if (!ModelState.IsValid) return View(model);

            // Determine the patient
            int patientId;
            if (User.IsInRole("Patient"))
            {
                var userId  = User.FindFirstValue(ClaimTypes.NameIdentifier);
                var patient = await _db.Patients.FirstOrDefaultAsync(p => p.UserId == userId);
                if (patient == null)
                {
                    TempData["Error"] = "Patient profile not found.";
                    return RedirectToAction("Index", "Home");
                }
                patientId = patient.Id;
            }
            else
            {
                if (model.PatientId == null)
                {
                    ModelState.AddModelError(nameof(model.PatientId), "Please pick a patient.");
                    return View(model);
                }
                patientId = model.PatientId.Value;
            }

            // Validate doctor schedule and overlap
            var doctor = await _db.Doctors
                .Include(d => d.Schedules)
                .Include(d => d.Leaves)
                .FirstOrDefaultAsync(d => d.Id == model.DoctorId);

            if (doctor == null)
            {
                ModelState.AddModelError(nameof(model.DoctorId), "Doctor not found.");
                return View(model);
            }

            // On leave?
            if (doctor.Leaves.Any(l => model.DateTimeStart >= l.StartDate && model.DateTimeStart <= l.EndDate.AddDays(1)))
            {
                ModelState.AddModelError(string.Empty, "Doctor is on leave on the selected date.");
                return View(model);
            }

            var dow = (int)model.DateTimeStart.DayOfWeek;
            var schedule = doctor.Schedules.FirstOrDefault(s =>
                s.IsActive && s.DayOfWeek == dow &&
                model.DateTimeStart.TimeOfDay >= s.StartTime &&
                model.DateTimeEnd.TimeOfDay   <= s.EndTime);

            if (schedule == null)
            {
                ModelState.AddModelError(string.Empty, "Doctor is not available at the requested time.");
                return View(model);
            }

            var overlap = await _db.Appointments.AnyAsync(a =>
                a.DoctorId == model.DoctorId &&
                a.Status != AppointmentStatus.Cancelled &&
                a.Status != AppointmentStatus.Missed &&
                a.DateTimeStart < model.DateTimeEnd &&
                a.DateTimeEnd   > model.DateTimeStart);

            if (overlap)
            {
                ModelState.AddModelError(string.Empty, "This slot is already booked for the doctor.");
                return View(model);
            }

            var appointment = new Appointment
            {
                PatientId     = patientId,
                DoctorId      = model.DoctorId,
                DateTimeStart = model.DateTimeStart,
                DateTimeEnd   = model.DateTimeEnd,
                Status        = AppointmentStatus.Requested,
                ReferenceCode = Guid.NewGuid().ToString("N")[..8].ToUpper(),
                Notes         = model.Notes,
                CreatedAt     = DateTime.UtcNow
            };
            _db.Appointments.Add(appointment);
            await _db.SaveChangesAsync();

            // Notify both sides
            var patientEntity = await _db.Patients.FindAsync(patientId);
            if (!string.IsNullOrEmpty(patientEntity?.UserId))
                await _notifications.CreateAsync(patientEntity.UserId,
                    $"Appointment #{appointment.ReferenceCode} booked for {appointment.DateTimeStart:f}.",
                    appointment.Id, patientId);
            if (!string.IsNullOrEmpty(doctor.UserId))
                await _notifications.CreateAsync(doctor.UserId,
                    $"New appointment requested by {patientEntity?.FullName} on {appointment.DateTimeStart:f}.",
                    appointment.Id);

            TempData["Success"] = $"Appointment booked. Reference: {appointment.ReferenceCode}";
            return RedirectToAction(nameof(Details), new { id = appointment.Id });
        }

        // ─── Lifecycle transitions ────────────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangeStatus(int id, AppointmentStatus newStatus, string? notes = null)
        {
            var appt = await _db.Appointments.FindAsync(id);
            if (appt == null) return NotFound();

            // Role gate for the action
            if (!await CanChangeStatusAsync(appt, newStatus))
                return Forbid();

            var (ok, error) = await _lifecycle.ChangeStatusAsync(id, newStatus, notes);
            if (!ok) TempData["Error"]   = error;
            else     TempData["Success"] = $"Appointment moved to {newStatus}.";

            return RedirectToAction(nameof(Details), new { id });
        }

        // ─── Authorisation helpers ────────────────────────────────────────────
        private async Task<bool> CanViewAppointmentAsync(Appointment appt)
        {
            if (User.IsInRole("ClinicManager") || User.IsInRole("Receptionist")) return true;

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (User.IsInRole("Patient"))
            {
                var patient = await _db.Patients.FirstOrDefaultAsync(p => p.UserId == userId);
                return patient != null && patient.Id == appt.PatientId;
            }
            if (User.IsInRole("Doctor"))
            {
                var doctor = await _db.Doctors.FirstOrDefaultAsync(d => d.UserId == userId);
                return doctor != null && doctor.Id == appt.DoctorId;
            }
            return false;
        }

        private async Task<bool> CanChangeStatusAsync(Appointment appt, AppointmentStatus newStatus)
        {
            // Cancel — patient can cancel own; staff can cancel any
            if (newStatus == AppointmentStatus.Cancelled)
            {
                if (User.IsInRole("ClinicManager") || User.IsInRole("Receptionist") || User.IsInRole("Doctor"))
                    return true;
                if (User.IsInRole("Patient"))
                {
                    var userId  = User.FindFirstValue(ClaimTypes.NameIdentifier);
                    var patient = await _db.Patients.FirstOrDefaultAsync(p => p.UserId == userId);
                    return patient != null && patient.Id == appt.PatientId;
                }
                return false;
            }

            // Confirm — doctor or receptionist
            if (newStatus == AppointmentStatus.Confirmed)
                return User.IsInRole("Doctor") || User.IsInRole("Receptionist") || User.IsInRole("ClinicManager");

            // Check-in — receptionist
            if (newStatus == AppointmentStatus.CheckedIn)
                return User.IsInRole("Receptionist") || User.IsInRole("ClinicManager");

            // In progress / Completed / Missed — doctor (or manager)
            return User.IsInRole("Doctor") || User.IsInRole("ClinicManager");
        }
    }
}

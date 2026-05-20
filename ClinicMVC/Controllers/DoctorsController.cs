using ClinicAPI.Data;
using ClinicAPI.Models.Domain;
using ClinicAPI.Models.Identity;
using ClinicMVC.Models;
using ClinicMVC.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicMVC.Controllers
{
    /// <summary>
    /// Doctor & specialization management. Only the Clinic Manager can mutate;
    /// the list view is also visible to receptionists for booking purposes.
    /// </summary>
    [Authorize]
    public class DoctorsController : Controller
    {
        private readonly ClinicDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly AppointmentLifecycleService _lifecycle;
        private readonly INotificationService _notifications;

        public DoctorsController(
            ClinicDbContext db,
            UserManager<ApplicationUser> userManager,
            AppointmentLifecycleService lifecycle,
            INotificationService notifications)
        {
            _db            = db;
            _userManager   = userManager;
            _lifecycle     = lifecycle;
            _notifications = notifications;
        }

        // ─── List (everyone can read) ─────────────────────────────────────────
        public async Task<IActionResult> Index(string? search = null, int page = 1)
        {
            const int pageSize = 10;
            var q = _db.Doctors
                .Include(d => d.Specializations).ThenInclude(s => s.Specialization)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
                q = q.Where(d => d.FullName.Contains(search) || d.LicenseNumber.Contains(search));

            var total = await q.CountAsync();
            var doctors = await q.OrderBy(d => d.FullName)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .ToListAsync();

            ViewBag.Page       = page;
            ViewBag.TotalPages = (int)Math.Ceiling(total / (double)pageSize);
            ViewBag.Search     = search;
            return View(doctors);
        }

        public async Task<IActionResult> Details(int id)
        {
            var doctor = await _db.Doctors
                .Include(d => d.Specializations).ThenInclude(s => s.Specialization)
                .Include(d => d.Schedules)
                .Include(d => d.Leaves)
                .FirstOrDefaultAsync(d => d.Id == id);

            if (doctor == null) return NotFound();
            return View(doctor);
        }

        // ─── Create (Manager only) ────────────────────────────────────────────
        [Authorize(Roles = "ClinicManager")]
        public async Task<IActionResult> Create()
        {
            ViewBag.Specializations = await _db.Specializations.ToListAsync();
            return View(new DoctorCreateViewModel());
        }

        [HttpPost, ValidateAntiForgeryToken]
        [Authorize(Roles = "ClinicManager")]
        public async Task<IActionResult> Create(DoctorCreateViewModel model)
        {
            ViewBag.Specializations = await _db.Specializations.ToListAsync();
            if (!ModelState.IsValid) return View(model);

            if (await _userManager.FindByEmailAsync(model.Email) != null)
            {
                ModelState.AddModelError(nameof(model.Email), "Email already in use.");
                return View(model);
            }

            var user = new ApplicationUser
            {
                UserName = model.Email,
                Email    = model.Email,
                FullName = model.FullName,
                CPR      = string.Empty
            };
            var create = await _userManager.CreateAsync(user, model.Password);
            if (!create.Succeeded)
            {
                foreach (var e in create.Errors) ModelState.AddModelError(string.Empty, e.Description);
                return View(model);
            }
            await _userManager.AddToRoleAsync(user, "Doctor");

            var doctor = new Doctor
            {
                UserId        = user.Id,
                FullName      = model.FullName,
                LicenseNumber = model.LicenseNumber,
                Email         = model.Email,
                Phone         = model.Phone
            };
            _db.Doctors.Add(doctor);
            await _db.SaveChangesAsync();

            foreach (var sid in model.SpecializationIds.Distinct())
            {
                _db.DoctorSpecializations.Add(new DoctorSpecialization
                {
                    DoctorId         = doctor.Id,
                    SpecializationId = sid
                });
            }
            await _db.SaveChangesAsync();

            TempData["Success"] = $"Doctor {doctor.FullName} created.";
            return RedirectToAction(nameof(Index));
        }

        // ─── Edit (Manager only) ──────────────────────────────────────────────
        [Authorize(Roles = "ClinicManager")]
        public async Task<IActionResult> Edit(int id)
        {
            var doctor = await _db.Doctors
                .Include(d => d.Specializations)
                .FirstOrDefaultAsync(d => d.Id == id);
            if (doctor == null) return NotFound();

            ViewBag.Specializations = await _db.Specializations.ToListAsync();
            return View(new DoctorEditViewModel
            {
                Id                = doctor.Id,
                FullName          = doctor.FullName,
                LicenseNumber     = doctor.LicenseNumber,
                Phone             = doctor.Phone,
                SpecializationIds = doctor.Specializations.Select(s => s.SpecializationId).ToList()
            });
        }

        [HttpPost, ValidateAntiForgeryToken]
        [Authorize(Roles = "ClinicManager")]
        public async Task<IActionResult> Edit(DoctorEditViewModel model)
        {
            ViewBag.Specializations = await _db.Specializations.ToListAsync();
            if (!ModelState.IsValid) return View(model);

            var doctor = await _db.Doctors
                .Include(d => d.Specializations)
                .FirstOrDefaultAsync(d => d.Id == model.Id);
            if (doctor == null) return NotFound();

            doctor.FullName      = model.FullName;
            doctor.LicenseNumber = model.LicenseNumber;
            doctor.Phone         = model.Phone;

            // Replace specializations
            _db.DoctorSpecializations.RemoveRange(doctor.Specializations);
            foreach (var sid in model.SpecializationIds.Distinct())
            {
                _db.DoctorSpecializations.Add(new DoctorSpecialization
                {
                    DoctorId         = doctor.Id,
                    SpecializationId = sid
                });
            }
            await _db.SaveChangesAsync();

            TempData["Success"] = "Doctor updated.";
            return RedirectToAction(nameof(Details), new { id = doctor.Id });
        }

        // ─── Delete (Manager only) ────────────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        [Authorize(Roles = "ClinicManager")]
        public async Task<IActionResult> Delete(int id)
        {
            var doctor = await _db.Doctors.FindAsync(id);
            if (doctor == null) return NotFound();

            // Refuse delete if there are non-terminal appointments
            var hasActive = await _db.Appointments.AnyAsync(a => a.DoctorId == id &&
                a.Status != AppointmentStatus.Cancelled &&
                a.Status != AppointmentStatus.Completed &&
                a.Status != AppointmentStatus.Missed);

            if (hasActive)
            {
                TempData["Error"] = "Cannot delete a doctor with active appointments. Cancel them first.";
                return RedirectToAction(nameof(Details), new { id });
            }

            _db.Doctors.Remove(doctor);
            await _db.SaveChangesAsync();
            TempData["Success"] = "Doctor removed.";
            return RedirectToAction(nameof(Index));
        }

        // ─── Schedules ────────────────────────────────────────────────────────
        [Authorize(Roles = "ClinicManager")]
        public async Task<IActionResult> AddSchedule(int doctorId)
        {
            if (!await _db.Doctors.AnyAsync(d => d.Id == doctorId)) return NotFound();
            return View(new ScheduleViewModel { DoctorId = doctorId });
        }

        [HttpPost, ValidateAntiForgeryToken]
        [Authorize(Roles = "ClinicManager")]
        public async Task<IActionResult> AddSchedule(ScheduleViewModel model)
        {
            if (model.EndTime <= model.StartTime)
                ModelState.AddModelError(nameof(model.EndTime), "End time must be after start time.");
            if (!ModelState.IsValid) return View(model);

            _db.Schedules.Add(new Schedule
            {
                DoctorId  = model.DoctorId,
                DayOfWeek = model.DayOfWeek,
                StartTime = model.StartTime,
                EndTime   = model.EndTime,
                IsActive  = model.IsActive
            });
            await _db.SaveChangesAsync();

            await NotifyAffectedAppointmentsAsync(model.DoctorId, model.DayOfWeek);

            TempData["Success"] = "Schedule slot added.";
            return RedirectToAction(nameof(Details), new { id = model.DoctorId });
        }

        [HttpPost, ValidateAntiForgeryToken]
        [Authorize(Roles = "ClinicManager")]
        public async Task<IActionResult> DeleteSchedule(int id)
        {
            var schedule = await _db.Schedules.FindAsync(id);
            if (schedule == null) return NotFound();

            var doctorId = schedule.DoctorId;
            var dayOfWeek = schedule.DayOfWeek;
            _db.Schedules.Remove(schedule);
            await _db.SaveChangesAsync();

            await NotifyAffectedAppointmentsAsync(doctorId, dayOfWeek);

            TempData["Success"] = "Schedule slot removed. Affected upcoming appointments were highlighted.";
            return RedirectToAction(nameof(Details), new { id = doctorId });
        }

        // ─── Leave ────────────────────────────────────────────────────────────
        [Authorize(Roles = "ClinicManager")]
        public async Task<IActionResult> AddLeave(int doctorId)
        {
            if (!await _db.Doctors.AnyAsync(d => d.Id == doctorId)) return NotFound();
            return View(new LeaveViewModel
            {
                DoctorId  = doctorId,
                StartDate = DateTime.UtcNow.Date.AddDays(1),
                EndDate   = DateTime.UtcNow.Date.AddDays(2)
            });
        }

        [HttpPost, ValidateAntiForgeryToken]
        [Authorize(Roles = "ClinicManager")]
        public async Task<IActionResult> AddLeave(LeaveViewModel model)
        {
            if (model.EndDate < model.StartDate)
                ModelState.AddModelError(nameof(model.EndDate), "End date must be on/after start date.");
            if (!ModelState.IsValid) return View(model);

            _db.Leaves.Add(new Leave
            {
                DoctorId  = model.DoctorId,
                StartDate = model.StartDate,
                EndDate   = model.EndDate,
                Reason    = model.Reason
            });
            await _db.SaveChangesAsync();

            // Auto-cancel appointments falling inside the leave window
            var affected = await _db.Appointments
                .Where(a => a.DoctorId == model.DoctorId
                        && a.DateTimeStart >= model.StartDate
                        && a.DateTimeStart <= model.EndDate.AddDays(1)
                        && a.Status != AppointmentStatus.Cancelled
                        && a.Status != AppointmentStatus.Completed
                        && a.Status != AppointmentStatus.Missed)
                .ToListAsync();

            foreach (var appt in affected)
            {
                await _lifecycle.ChangeStatusAsync(appt.Id, AppointmentStatus.Cancelled,
                    $"Auto-cancelled: doctor on leave ({model.StartDate:d} – {model.EndDate:d}).");
            }

            TempData["Success"] = $"Leave saved. {affected.Count} affected appointment(s) auto-cancelled and patients notified.";
            return RedirectToAction(nameof(Details), new { id = model.DoctorId });
        }

        [HttpPost, ValidateAntiForgeryToken]
        [Authorize(Roles = "ClinicManager")]
        public async Task<IActionResult> DeleteLeave(int id)
        {
            var leave = await _db.Leaves.FindAsync(id);
            if (leave == null) return NotFound();
            var doctorId = leave.DoctorId;
            _db.Leaves.Remove(leave);
            await _db.SaveChangesAsync();
            TempData["Success"] = "Leave removed.";
            return RedirectToAction(nameof(Details), new { id = doctorId });
        }

        // ─── Edge case: notify patients of appointments that may be affected
        // when the doctor's weekly schedule changes ─────────────────────────
        private async Task NotifyAffectedAppointmentsAsync(int doctorId, int dayOfWeek)
        {
            var upcoming = await _db.Appointments
                .Include(a => a.Patient).ThenInclude(p => p!.User)
                .Where(a => a.DoctorId == doctorId
                         && a.DateTimeStart > DateTime.UtcNow
                         && (int)a.DateTimeStart.DayOfWeek == dayOfWeek
                         && a.Status != AppointmentStatus.Cancelled
                         && a.Status != AppointmentStatus.Completed
                         && a.Status != AppointmentStatus.Missed)
                .ToListAsync();

            foreach (var appt in upcoming)
            {
                if (appt.Patient?.UserId is { Length: > 0 } pUid)
                {
                    await _notifications.CreateAsync(
                        pUid,
                        $"Your doctor's schedule has changed — please review appointment #{appt.ReferenceCode}.",
                        appt.Id, appt.PatientId);
                }
            }
        }

        // ─── AJAX: list doctors for a specialization (booking dropdowns) ─────
        [AllowAnonymous]
        public async Task<IActionResult> BySpecialization(int specializationId)
        {
            var doctors = await _db.DoctorSpecializations
                .Where(ds => ds.SpecializationId == specializationId)
                .Select(ds => new
                {
                    Id   = ds.DoctorId,
                    Name = ds.Doctor!.FullName
                })
                .ToListAsync();
            return Json(doctors);
        }
    }
}

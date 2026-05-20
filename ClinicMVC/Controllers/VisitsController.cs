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
    /// <summary>
    /// Doctor records diagnosis / treatment / prescriptions for an
    /// appointment they own. The visit record was auto-created when the
    /// appointment moved to "Completed" by the lifecycle service.
    /// </summary>
    [Authorize(Roles = "Doctor,ClinicManager")]
    public class VisitsController : Controller
    {
        private readonly ClinicDbContext _db;
        private readonly INotificationService _notifications;

        public VisitsController(ClinicDbContext db, INotificationService notifications)
        {
            _db            = db;
            _notifications = notifications;
        }

        // GET /Visits/Edit/{appointmentId}
        public async Task<IActionResult> Edit(int appointmentId)
        {
            var appt = await _db.Appointments
                .Include(a => a.Patient)
                .Include(a => a.Doctor)
                .Include(a => a.VisitRecord)!.ThenInclude(v => v!.Prescriptions)
                .FirstOrDefaultAsync(a => a.Id == appointmentId);

            if (appt == null) return NotFound();
            if (!await CanEditVisitAsync(appt)) return Forbid();
            if (appt.Status != AppointmentStatus.Completed)
            {
                TempData["Error"] = "Visit records can only be edited after an appointment is completed.";
                return RedirectToAction("Details", "Appointments", new { id = appointmentId });
            }

            var vm = new VisitRecordViewModel
            {
                AppointmentId   = appt.Id,
                VisitRecordId   = appt.VisitRecord?.Id,
                PatientName     = appt.Patient?.FullName ?? "",
                DoctorName      = appt.Doctor?.FullName ?? "",
                AppointmentDate = appt.DateTimeStart,
                Diagnosis       = appt.VisitRecord?.Diagnosis,
                Treatment       = appt.VisitRecord?.Treatment,
                DoctorNotes     = appt.VisitRecord?.DoctorNotes,
                Prescriptions   = appt.VisitRecord?.Prescriptions
                    .Select(p => new PrescriptionLineViewModel
                    {
                        Id             = p.Id,
                        MedicationName = p.MedicationName,
                        Dosage         = p.Dosage,
                        Frequency      = p.Frequency,
                        Duration       = p.Duration,
                        Instructions   = p.Instructions
                    }).ToList() ?? new List<PrescriptionLineViewModel>()
            };

            return View(vm);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(VisitRecordViewModel model)
        {
            var appt = await _db.Appointments
                .Include(a => a.Patient)
                .Include(a => a.VisitRecord)!.ThenInclude(v => v!.Prescriptions)
                .FirstOrDefaultAsync(a => a.Id == model.AppointmentId);

            if (appt == null) return NotFound();
            if (!await CanEditVisitAsync(appt)) return Forbid();

            if (!ModelState.IsValid) return View(model);

            // Ensure a VisitRecord exists
            var visit = appt.VisitRecord;
            if (visit == null)
            {
                visit = new VisitRecord
                {
                    AppointmentId = appt.Id,
                    CreatedAt     = DateTime.UtcNow
                };
                _db.VisitRecords.Add(visit);
                await _db.SaveChangesAsync();
            }

            visit.Diagnosis   = model.Diagnosis;
            visit.Treatment   = model.Treatment;
            visit.DoctorNotes = model.DoctorNotes;

            // Replace prescription list (simple full-replace strategy)
            if (visit.Prescriptions.Any())
                _db.Prescriptions.RemoveRange(visit.Prescriptions);

            foreach (var line in model.Prescriptions
                         .Where(p => !string.IsNullOrWhiteSpace(p.MedicationName)))
            {
                _db.Prescriptions.Add(new Prescription
                {
                    VisitRecordId  = visit.Id,
                    MedicationName = line.MedicationName.Trim(),
                    Dosage         = line.Dosage,
                    Frequency      = line.Frequency,
                    Duration       = line.Duration,
                    Instructions   = line.Instructions
                });
            }

            await _db.SaveChangesAsync();

            if (!string.IsNullOrEmpty(appt.Patient?.UserId))
                await _notifications.CreateAsync(
                    appt.Patient.UserId,
                    $"Your visit record for appointment #{appt.ReferenceCode} has been updated.",
                    appt.Id, appt.PatientId);

            TempData["Success"] = "Visit record and prescriptions saved.";
            return RedirectToAction("Details", "Appointments", new { id = appt.Id });
        }

        private async Task<bool> CanEditVisitAsync(Appointment appt)
        {
            if (User.IsInRole("ClinicManager")) return true;
            if (!User.IsInRole("Doctor")) return false;

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var doctor = await _db.Doctors.FirstOrDefaultAsync(d => d.UserId == userId);
            return doctor != null && doctor.Id == appt.DoctorId;
        }
    }
}

using System.Security.Claims;
using ClinicAPI.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicMVC.Controllers
{
    [Authorize]
    public class PatientsController : Controller
    {
        private readonly ClinicDbContext _db;
        public PatientsController(ClinicDbContext db) { _db = db; }

        // Receptionist / Manager — paged list with search
        [Authorize(Roles = "Receptionist,ClinicManager,Doctor")]
        public async Task<IActionResult> Index(string? search = null, int page = 1)
        {
            const int pageSize = 10;
            var q = _db.Patients.AsQueryable();
            if (!string.IsNullOrWhiteSpace(search))
                q = q.Where(p => p.FullName.Contains(search) || p.CPR.Contains(search));

            var total = await q.CountAsync();
            var list  = await q.OrderBy(p => p.FullName)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .ToListAsync();

            ViewBag.Page       = page;
            ViewBag.TotalPages = (int)Math.Ceiling(total / (double)pageSize);
            ViewBag.Search     = search;
            return View(list);
        }

        // Profile + visit history
        public async Task<IActionResult> Profile(int? id = null)
        {
            int patientId;
            if (id == null)
            {
                // Patient viewing own profile
                if (!User.IsInRole("Patient")) return BadRequest();
                var userId  = User.FindFirstValue(ClaimTypes.NameIdentifier);
                var patient = await _db.Patients.FirstOrDefaultAsync(p => p.UserId == userId);
                if (patient == null) return NotFound();
                patientId = patient.Id;
            }
            else
            {
                patientId = id.Value;
            }

            var record = await _db.Patients
                .Include(p => p.Appointments).ThenInclude(a => a.Doctor)
                .Include(p => p.Appointments).ThenInclude(a => a.VisitRecord)!.ThenInclude(v => v!.Prescriptions)
                .FirstOrDefaultAsync(p => p.Id == patientId);

            if (record == null) return NotFound();

            // Doctors can only see patients they've treated
            if (User.IsInRole("Doctor"))
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                var doctor = await _db.Doctors.FirstOrDefaultAsync(d => d.UserId == userId);
                if (doctor == null) return Forbid();
                var hasShared = record.Appointments.Any(a => a.DoctorId == doctor.Id);
                if (!hasShared) return Forbid();
            }

            // Patient can only see own profile
            if (User.IsInRole("Patient"))
            {
                var userId  = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (record.UserId != userId) return Forbid();
            }

            return View(record);
        }
    }
}

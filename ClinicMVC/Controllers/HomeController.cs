using System.Diagnostics;
using ClinicAPI.Data;
using ClinicAPI.Models.Domain;
using ClinicMVC.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicMVC.Controllers
{
    public class HomeController : Controller
    {
        private readonly ClinicDbContext _db;

        public HomeController(ClinicDbContext db) { _db = db; }

        public async Task<IActionResult> Index()
        {
            if (!User.Identity?.IsAuthenticated ?? true) return View("Landing");

            // Per-role dashboard widgets
            if (User.IsInRole("ClinicManager"))
            {
                ViewBag.TotalDoctors      = await _db.Doctors.CountAsync();
                ViewBag.TotalPatients     = await _db.Patients.CountAsync();
                ViewBag.TotalAppointments = await _db.Appointments.CountAsync();
                ViewBag.TodayAppointments = await _db.Appointments
                    .CountAsync(a => a.DateTimeStart.Date == DateTime.UtcNow.Date);
                return View("DashboardManager");
            }

            if (User.IsInRole("Doctor"))
            {
                var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                var doctor = await _db.Doctors.FirstOrDefaultAsync(d => d.UserId == userId);
                ViewBag.DoctorId = doctor?.Id ?? 0;
                ViewBag.TodayCount = doctor == null ? 0 :
                    await _db.Appointments.CountAsync(a => a.DoctorId == doctor.Id
                        && a.DateTimeStart.Date == DateTime.UtcNow.Date);
                return View("DashboardDoctor");
            }

            if (User.IsInRole("Receptionist"))
            {
                ViewBag.TodayAppointments = await _db.Appointments
                    .CountAsync(a => a.DateTimeStart.Date == DateTime.UtcNow.Date);
                ViewBag.PendingConfirmations = await _db.Appointments
                    .CountAsync(a => a.Status == AppointmentStatus.Requested);
                return View("DashboardReceptionist");
            }

            // Patient
            return View("DashboardPatient");
        }

        [AllowAnonymous]
        public IActionResult Privacy() => View();

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}

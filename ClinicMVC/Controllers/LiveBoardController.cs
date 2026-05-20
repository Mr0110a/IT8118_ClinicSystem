using ClinicAPI.Data;
using ClinicAPI.Models.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicMVC.Controllers
{
    /// <summary>
    /// Live waiting-room / receptionist board powered by SignalR.
    /// Initial snapshot rendered server-side; pushes via the AppointmentHub.
    /// </summary>
    [Authorize(Roles = "Receptionist,ClinicManager,Doctor")]
    public class LiveBoardController : Controller
    {
        private readonly ClinicDbContext _db;
        public LiveBoardController(ClinicDbContext db) { _db = db; }

        public async Task<IActionResult> Index()
        {
            var today = DateTime.UtcNow.Date;
            var tomorrow = today.AddDays(1);

            var board = await _db.Appointments
                .Include(a => a.Patient)
                .Include(a => a.Doctor)
                .Where(a => a.DateTimeStart >= today && a.DateTimeStart < tomorrow
                         && a.Status != AppointmentStatus.Cancelled
                         && a.Status != AppointmentStatus.Missed)
                .OrderBy(a => a.DateTimeStart)
                .ToListAsync();

            return View(board);
        }
    }
}

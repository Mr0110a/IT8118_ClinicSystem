using System.Security.Claims;
using ClinicAPI.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicMVC.Controllers
{
    [Authorize]
    public class NotificationsController : Controller
    {
        private readonly ClinicDbContext _db;
        public NotificationsController(ClinicDbContext db) { _db = db; }

        public async Task<IActionResult> Index()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var list = await _db.Notifications
                .Where(n => n.UserId == userId)
                .OrderByDescending(n => n.CreatedAt)
                .Take(100)
                .ToListAsync();
            return View(list);
        }

        // Used by the bell badge (AJAX poll fallback) and the layout
        public async Task<IActionResult> UnreadCount()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var count  = userId == null ? 0
                : await _db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead);
            return Json(new { count });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkRead(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var n = await _db.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
            if (n == null) return NotFound();
            n.IsRead = true;
            await _db.SaveChangesAsync();

            // If notification has a related appointment, jump there
            if (n.RelatedAppointmentId.HasValue)
                return RedirectToAction("Details", "Appointments", new { id = n.RelatedAppointmentId });
            return RedirectToAction(nameof(Index));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkAllRead()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var notes  = await _db.Notifications
                .Where(n => n.UserId == userId && !n.IsRead).ToListAsync();
            foreach (var n in notes) n.IsRead = true;
            await _db.SaveChangesAsync();
            TempData["Success"] = $"{notes.Count} notification(s) marked as read.";
            return RedirectToAction(nameof(Index));
        }
    }
}

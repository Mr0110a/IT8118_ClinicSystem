using ClinicAPI.Models.Identity;
using ClinicMVC.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClinicMVC.Controllers
{
    /// <summary>
    /// Manager-only user & role management. The brief notes that there is no
    /// Administrator role; the Clinic Manager carries elevated privileges.
    /// </summary>
    [Authorize(Roles = "ClinicManager")]
    public class UsersController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;

        public UsersController(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager)
        {
            _userManager = userManager;
            _roleManager = roleManager;
        }

        public async Task<IActionResult> Index(string? search = null)
        {
            var users = _userManager.Users.AsQueryable();
            if (!string.IsNullOrWhiteSpace(search))
                users = users.Where(u => u.Email!.Contains(search) || u.FullName.Contains(search));

            var list = await users.OrderBy(u => u.FullName).ToListAsync();
            var vms = new List<UserListViewModel>();
            foreach (var u in list)
            {
                var roles = await _userManager.GetRolesAsync(u);
                vms.Add(new UserListViewModel
                {
                    Id       = u.Id,
                    Email    = u.Email ?? "",
                    FullName = u.FullName,
                    CPR      = u.CPR,
                    Role     = roles.FirstOrDefault() ?? "(none)"
                });
            }
            ViewBag.Search = search;
            return View(vms);
        }

        public async Task<IActionResult> EditRole(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            var roles       = await _userManager.GetRolesAsync(user);
            var allRoles    = _roleManager.Roles.Select(r => r.Name!).ToList();
            ViewBag.AllRoles = allRoles;
            return View(new EditUserRoleViewModel
            {
                UserId      = user.Id,
                Email       = user.Email ?? "",
                FullName    = user.FullName,
                CurrentRole = roles.FirstOrDefault() ?? "(none)",
                NewRole     = roles.FirstOrDefault() ?? ""
            });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> EditRole(EditUserRoleViewModel model)
        {
            var user = await _userManager.FindByIdAsync(model.UserId);
            if (user == null) return NotFound();

            ViewBag.AllRoles = _roleManager.Roles.Select(r => r.Name!).ToList();
            if (!ModelState.IsValid) return View(model);

            var current = await _userManager.GetRolesAsync(user);
            if (current.Any())
                await _userManager.RemoveFromRolesAsync(user, current);

            if (!await _roleManager.RoleExistsAsync(model.NewRole))
                await _roleManager.CreateAsync(new IdentityRole(model.NewRole));

            await _userManager.AddToRoleAsync(user, model.NewRole);
            TempData["Success"] = $"{user.FullName}'s role updated to {model.NewRole}.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            // Safety: don't allow deleting your own account
            if (user.UserName == User.Identity?.Name)
            {
                TempData["Error"] = "You cannot delete the account you are signed in with.";
                return RedirectToAction(nameof(Index));
            }

            await _userManager.DeleteAsync(user);
            TempData["Success"] = "User deleted.";
            return RedirectToAction(nameof(Index));
        }
    }
}

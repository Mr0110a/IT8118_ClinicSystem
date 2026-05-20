using ClinicAPI.Data;
using ClinicAPI.Models.Domain;
using ClinicAPI.Models.Identity;
using ClinicMVC.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace ClinicMVC.Controllers
{
    public class AccountController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly ClinicDbContext _db;

        // Roles end users may self-register as. Manager/Doctor accounts are
        // created by the Clinic Manager from the Users / Doctors pages.
        private static readonly string[] PublicSelfSignUpRoles = { "Patient" };
        private static readonly string[] AllRoles = { "Patient", "Doctor", "Receptionist", "ClinicManager" };

        public AccountController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            RoleManager<IdentityRole> roleManager,
            ClinicDbContext db)
        {
            _userManager   = userManager;
            _signInManager = signInManager;
            _roleManager   = roleManager;
            _db            = db;
        }

        // ─── Register ─────────────────────────────────────────────────────────
        [HttpGet]
        [AllowAnonymous]
        public IActionResult Register()
        {
            ViewBag.AvailableRoles = PublicSelfSignUpRoles;
            return View(new RegisterViewModel { Role = "Patient" });
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            ViewBag.AvailableRoles = PublicSelfSignUpRoles;

            if (!PublicSelfSignUpRoles.Contains(model.Role))
            {
                ModelState.AddModelError(nameof(model.Role),
                    "Only patient accounts can be created from this page. Staff accounts are created by the Clinic Manager.");
            }

            if (model.Role == "Patient" && string.IsNullOrWhiteSpace(model.CPR))
                ModelState.AddModelError(nameof(model.CPR), "CPR is required for patients.");

            if (!ModelState.IsValid) return View(model);

            // Ensure role exists (it does — seeded in DbContext — but guard anyway)
            if (!await _roleManager.RoleExistsAsync(model.Role))
                await _roleManager.CreateAsync(new IdentityRole(model.Role));

            var existing = await _userManager.FindByEmailAsync(model.Email);
            if (existing != null)
            {
                ModelState.AddModelError(nameof(model.Email), "An account with this email already exists.");
                return View(model);
            }

            var user = new ApplicationUser
            {
                UserName = model.Email,
                Email    = model.Email,
                FullName = model.FullName,
                CPR      = model.CPR ?? string.Empty
            };

            var result = await _userManager.CreateAsync(user, model.Password);
            if (!result.Succeeded)
            {
                foreach (var err in result.Errors)
                    ModelState.AddModelError(string.Empty, err.Description);
                return View(model);
            }

            await _userManager.AddToRoleAsync(user, model.Role);

            // Create the matching Patient row
            if (model.Role == "Patient")
            {
                _db.Patients.Add(new Patient
                {
                    UserId           = user.Id,
                    CPR              = model.CPR!,
                    FullName         = model.FullName,
                    Phone            = model.Phone,
                    Email            = model.Email,
                    DateOfBirth      = model.DateOfBirth ?? DateTime.UtcNow.AddYears(-30),
                    ReferenceNumber  = $"PAT-{Guid.NewGuid().ToString()[..6].ToUpper()}"
                });
                await _db.SaveChangesAsync();
            }

            await _signInManager.SignInAsync(user, isPersistent: false);
            TempData["Success"] = "Account created — welcome!";
            return RedirectToAction("Index", "Home");
        }

        // ─── Login ────────────────────────────────────────────────────────────
        [HttpGet]
        [AllowAnonymous]
        public IActionResult Login(string? returnUrl = null)
        {
            return View(new LoginViewModel { ReturnUrl = returnUrl });
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var result = await _signInManager.PasswordSignInAsync(
                model.Email, model.Password, model.RememberMe, lockoutOnFailure: false);

            if (!result.Succeeded)
            {
                ModelState.AddModelError(string.Empty, "Invalid email or password.");
                return View(model);
            }

            if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
                return Redirect(model.ReturnUrl);

            return RedirectToAction("Index", "Home");
        }

        // ─── Logout ───────────────────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction("Index", "Home");
        }

        [AllowAnonymous]
        public IActionResult AccessDenied() => View();
    }
}

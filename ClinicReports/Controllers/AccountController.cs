using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;

namespace ClinicReports.Controllers
{
    public class AccountController : Controller
    {
        private readonly IHttpClientFactory _clientFactory;

        public AccountController(IHttpClientFactory clientFactory)
        {
            _clientFactory = clientFactory;
        }

        [HttpGet]
        public IActionResult Login() => View();

        [HttpPost]
        public async Task<IActionResult> Login(string email, string password)
        {
            var client = _clientFactory.CreateClient();

            // Call your teammates' Auth API endpoint directly
            var response = await client.PostAsJsonAsync("https://localhost:7001/api/auth/login", new { email, password });

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<LoginResponse>();

                if (result != null && result.Role == "ClinicManager")
                {
                    // Save the secure token to Session state storage
                    HttpContext.Session.SetString("JWToken", result.Token);
                    return RedirectToAction("Index", "Dashboard");
                }

                ModelState.AddModelError("", "Access Denied: You are not authorized as a Clinic Manager.");
                return View();
            }

            ModelState.AddModelError("", "Invalid login credentials.");
            return View();
        }

        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Login");
        }
    }

    public class LoginResponse
    {
        public string Token { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
    }
}
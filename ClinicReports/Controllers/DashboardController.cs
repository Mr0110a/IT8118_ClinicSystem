using ClinicReports.Models;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;
using System.Text.Json;

namespace ClinicReports.Controllers
{
    public class DashboardController : Controller
    {
        private readonly IHttpClientFactory _clientFactory;

        public DashboardController(IHttpClientFactory clientFactory)
        {
            _clientFactory = clientFactory;
        }

        public async Task<IActionResult> Index()
        {
            // Create a specialized secure client configured to auto-attach our JWT tokens
            var client = _clientFactory.CreateClient("SecureClinicApiClient");

            try
            {
                // 1. Fetch Stats (This one works fine!)
                var stats = await client.GetFromJsonAsync<AppointmentStatsDto>("api/reports/appointment-stats");

                // 2. Fetch Doctor Utilisation (Reach inside the "doctors" array property)
                var utilisationNode = await client.GetFromJsonAsync<System.Text.Json.Nodes.JsonNode>("api/reports/doctor-utilisation");
                var utilisation = utilisationNode?["doctors"].Deserialize<List<DoctorUtilisationDto>>() ?? new List<DoctorUtilisationDto>();

                // 3. Fetch Cancellation Rate (The backend returns a single summary object, not a list!)
                var cancellationsDto = await client.GetFromJsonAsync<CancellationRateDto>("api/reports/cancellation-rate");
                // If your frontend View specifically loops through a list, wrap this single item inside one:
                var cancellations = new List<CancellationRateDto> { cancellationsDto! };

                ViewBag.UtilisationData = utilisation;
                ViewBag.CancellationData = cancellations;

                return View(stats);
            }
            catch (HttpRequestException ex)
            {
                // If the API says unauthorized, redirect back out to login portal immediately
                return RedirectToAction("Login", "Account");
            }
        }
    }
}
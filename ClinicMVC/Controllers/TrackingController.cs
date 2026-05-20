using System.Text.Json;
using ClinicMVC.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicMVC.Controllers
{
    /// <summary>
    /// PUBLIC tracking lookup. Per the brief, this is the ONLY MVC feature
    /// that consumes the Web API via HttpClient. All other features in the MVC
    /// app use EF Core directly through the shared ClinicDbContext.
    /// </summary>
    [AllowAnonymous]
    public class TrackingController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<TrackingController> _logger;

        public TrackingController(IHttpClientFactory httpClientFactory, ILogger<TrackingController> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger            = logger;
        }

        // GET /Tracking
        public IActionResult Index() => View(new TrackingLookupViewModel());

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Lookup(TrackingLookupViewModel form)
        {
            if (!ModelState.IsValid) return View(nameof(Index), form);

            var result = new TrackingResultViewModel();

            try
            {
                var client = _httpClientFactory.CreateClient("ClinicApi");
                // No JWT — the tracking endpoint is [AllowAnonymous] on the API
                var response = await client.GetAsync($"api/tracking/{Uri.EscapeDataString(form.CPR)}/{Uri.EscapeDataString(form.ReferenceCode)}");

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    result.Found        = false;
                    result.ErrorMessage = "No matching appointment found for the given CPR and reference code.";
                    return View(nameof(Result), result);
                }

                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync();

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                result.PatientName = root.TryGetProperty("patientName", out var pn) ? pn.GetString() : null;
                result.Found       = true;

                if (root.TryGetProperty("appointments", out var apps) && apps.ValueKind == JsonValueKind.Array)
                {
                    foreach (var a in apps.EnumerateArray())
                    {
                        result.Appointments.Add(new TrackingAppointment
                        {
                            Id            = a.TryGetProperty("id", out var idP) ? idP.GetInt32() : 0,
                            StartTime     = a.TryGetProperty("startTime", out var s) ? s.GetDateTime() : default,
                            EndTime       = a.TryGetProperty("endTime", out var e) ? e.GetDateTime() : default,
                            DoctorName    = a.TryGetProperty("doctorName", out var d) ? d.GetString() : null,
                            Status        = a.TryGetProperty("status", out var st) ? st.GetString() : null,
                            ReferenceCode = a.TryGetProperty("referenceCode", out var rc) ? rc.GetString() : null
                        });
                    }
                }

                if (root.TryGetProperty("lastVisit", out var lv) && lv.ValueKind == JsonValueKind.Object)
                {
                    result.LastVisit = new TrackingVisit
                    {
                        Diagnosis = lv.TryGetProperty("diagnosis", out var dg) ? dg.GetString() : null,
                        Treatment = lv.TryGetProperty("treatment", out var tr) ? tr.GetString() : null,
                        VisitDate = lv.TryGetProperty("visitDate", out var vd) ? vd.GetDateTime() : default
                    };
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Tracking API call failed");
                result.Found        = false;
                result.ErrorMessage = "Could not reach the lookup service. Please try again later.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tracking lookup unexpected error");
                result.Found        = false;
                result.ErrorMessage = "An unexpected error occurred.";
            }

            return View(nameof(Result), result);
        }

        public IActionResult Result() => View(new TrackingResultViewModel { Found = false });
    }
}

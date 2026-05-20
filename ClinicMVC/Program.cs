using ClinicAPI.Data;
using ClinicAPI.Models.Identity;
using ClinicMVC.Hubs;
using ClinicMVC.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ─── Database (shared from ClinicAPI project) ─────────────────────────────────
// The MVC app uses the same ClinicDbContext from the API project, per the
// assessment brief (the API project is the shared data layer).
builder.Services.AddDbContext<ClinicDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ─── ASP.NET Core Identity with Cookie Authentication ─────────────────────────
// Identity stores live in ClinicDbContext (shared with the API).
// Cookie auth is used for the MVC app; the API uses JWT for its endpoints.
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequireDigit           = true;
    options.Password.RequiredLength         = 6;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase       = false;
    options.User.RequireUniqueEmail         = true;
})
.AddEntityFrameworkStores<ClinicDbContext>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath        = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.LogoutPath       = "/Account/Logout";
    options.ExpireTimeSpan   = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});

// ─── MVC + Views ──────────────────────────────────────────────────────────────
// IMPORTANT: limit controller discovery to THIS project's assembly only.
// Without this, ASP.NET Core would also register the API project's controllers
// (because we have a ProjectReference to ClinicAPI for the shared data layer),
// which collides with our MVC controllers of the same name (e.g. Appointments)
// and causes HTTP 405 on otherwise-correct routes.
builder.Services
    .AddControllersWithViews()
    .ConfigureApplicationPartManager(apm =>
    {
        // Remove any application parts that came from the ClinicAPI assembly.
        var apiAssemblyName = typeof(ClinicAPI.Data.ClinicDbContext).Assembly.GetName().Name;
        var partsToRemove = apm.ApplicationParts
            .Where(part => part.Name == apiAssemblyName)
            .ToList();
        foreach (var part in partsToRemove)
            apm.ApplicationParts.Remove(part);
    });

// ─── SignalR (real-time appointment tracking + notifications) ────────────────
builder.Services.AddSignalR();

// ─── HttpClient (ONLY used by the public tracking page per the brief) ───────
// Named client points at the Web API base URL.
builder.Services.AddHttpClient("ClinicApi", client =>
{
    var baseUrl = builder.Configuration["ClinicApi:BaseUrl"] ?? "https://localhost:7001";
    client.BaseAddress = new Uri(baseUrl);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});

// ─── Application services ────────────────────────────────────────────────────
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<AppointmentLifecycleService>();

var app = builder.Build();

// ─── Ensure database exists and is seeded ─────────────────────────────────────
// Runs pending EF migrations and calls the same seeder as the API. This means
// you can launch only the MVC project (without the API) and login will still
// work, because the AspNetUsers / Patients / Doctors tables will be created.
using (var scope = app.Services.CreateScope())
{
    try
    {
        await ClinicAPI.Services.DbInitializer.InitializeAsync(scope.ServiceProvider);
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Error while initialising the database.");
    }
}

// ─── Pipeline ─────────────────────────────────────────────────────────────────
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// SignalR hubs
app.MapHub<AppointmentHub>("/hubs/appointments");
app.MapHub<NotificationHub>("/hubs/notifications");

app.Run();

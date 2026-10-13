
using Microsoft.EntityFrameworkCore;
using OmsCafe.Data;
using OmsCafe.Hubs;

var builder = WebApplication.CreateBuilder(args);

// ── Services ──────────────────────────────────────────────
builder.Services.AddRazorPages();
builder.Services.AddControllers();
builder.Services.AddSignalR();

builder.Services.AddDistributedMemoryCache();

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(60);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "RequestVerificationToken";
    options.Cookie.Name = "XSRF-TOKEN";
});

// ── Database Connection ───────────────────────────────────
// Reads the CafeOrderingDB connection string from appsettings.json.
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("CafeOrderingDB")));

// ── Localhost Configuration ──────────────────────────────
// Runs the application on http://localhost:5114.
builder.WebHost.UseUrls("http://localhost:5114");

// ── Build Application ─────────────────────────────────────
var app = builder.Build();

// ── Database Migration ───────────────────────────────────
// Applies pending EF Core migrations at application startup.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

// ── Middleware Pipeline ───────────────────────────────────
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// Uncomment if you configure HTTPS locally.
// app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();

app.UseSession();
app.UseAuthorization();

// ── Endpoints ─────────────────────────────────────────────
app.MapRazorPages();
app.MapControllers();
app.MapHub<TableHub>("/tableHub");

// ── Run Application ───────────────────────────────────────
app.Run();
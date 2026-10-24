using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using OmsCafe.Data;
using OmsCafe.Hubs;

var builder = WebApplication.CreateBuilder(args);

// ── Services ──────────────────────────────────────────────
builder.Services.AddRazorPages();
builder.Services.AddControllers();
builder.Services.AddSignalR();

// Sessions are stored in SQL Server so they survive app restarts/recycling.
// (Requires the SessionCache table and the Microsoft.Extensions.Caching.SqlServer package.)
builder.Services.AddDistributedSqlServerCache(o =>
{
    o.ConnectionString = builder.Configuration.GetConnectionString("CafeOrderingDB");
    o.SchemaName = "dbo";
    o.TableName = "SessionCache";
});

// Keep the encryption keys on disk so the session/antiforgery cookies
// stay valid after the app restarts.
var keysFolder = new DirectoryInfo(
    Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys"));
keysFolder.Create(); // creates the folder if it doesn't exist

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(keysFolder)
    .SetApplicationName("OmsCafe");

builder.Services.AddScoped<OmsCafe.Services.ShiftService>();
builder.Services.AddSingleton<OmsCafe.Services.LoginAttemptService>(); 
//builder.Services.AddSingleton<OmsCafe.Services.Passworpolicy>(); // ← add this

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
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("CafeOrderingDB")));

// ── Localhost URL (development only) ──────────────────────
if (builder.Environment.IsDevelopment())
{
    builder.WebHost.UseUrls("http://localhost:5114");
}

// ── Build Application ─────────────────────────────────────
var app = builder.Build();

// ── Database Migration (disabled for hosting) ─────────────
// The hosted database was created from a script, so the tables already exist.
// using (var scope = app.Services.CreateScope())
// {
//     var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
//     db.Database.Migrate();
// }

// ── Middleware Pipeline ───────────────────────────────────
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();

app.UseSession();

// ── TEMPORARY DEBUG: remove after testing ─────────────────
var startedAt = DateTime.UtcNow;
app.MapGet("/debug/alive", (HttpContext c) => Results.Json(new
{
    startedAt,
    pid = Environment.ProcessId,
    session = c.Session.Id,
    hasCookie = c.Request.Cookies.ContainsKey(".AspNetCore.Session"),
    userId = c.Session.GetString("UserId"),
    scheme = c.Request.Scheme,
    host = c.Request.Host.Value
}));
// ──────────────────────────────────────────────────────────

// Session guard: Admin/Cashier pages require a logged-in session,
// and are never cached by the browser (so Back after logout can't show them).
app.Use(async (ctx, next) =>
{
    var path = ctx.Request.Path.Value ?? "";

    bool isProtected =
        path.StartsWith("/Admin", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/Cashier", StringComparison.OrdinalIgnoreCase);

    if (isProtected)
    {
        ctx.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
        ctx.Response.Headers["Pragma"] = "no-cache";

        if (string.IsNullOrEmpty(ctx.Session.GetString("UserId")))
        {
            ctx.Response.Redirect("/Login");
            return;
        }
    }

    await next();
});

app.UseAuthorization();

// ── Endpoints ─────────────────────────────────────────────
app.MapRazorPages();
app.MapControllers();
app.MapHub<TableHub>("/tableHub");

app.Run();
using Microsoft.EntityFrameworkCore;
using OmsCafe.Data;
using OmsCafe.Hubs;


var builder = WebApplication.CreateBuilder(args);

// ── Services ───────────────────────────────────────────────────────
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

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("CafeOrderingDB")));

builder.WebHost.UseUrls("http://0.0.0.0:5114");

// ── Pipeline ───────────────────────────────────────────────────────
var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// app.UseHttpsRedirection();

app.UseRouting();
app.UseSession();       // ← must be after UseRouting, before UseAuthorization
app.UseAuthorization();

app.UseStaticFiles();

app.MapRazorPages();
app.MapControllers();

app.MapHub<TableHub>("/tableHub");

app.Run();
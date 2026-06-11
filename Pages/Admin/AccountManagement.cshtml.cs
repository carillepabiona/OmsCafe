using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OmsCafe.Data;
using OmsCafe.Models;

public class AccountManagementModel : PageModel
{
    private readonly AppDbContext _db;

    public AccountManagementModel(AppDbContext db)
    {
        _db = db;
    }

    public List<User> Users { get; set; } = new();
    public List<Role> Roles { get; set; } = new();
    public int TotalAccounts { get; set; }
    public int ActiveCount { get; set; }
    public int InactiveCount { get; set; }
    public int AdminCount { get; set; }
    public string? AddError { get; set; }

    public int CurrentPage { get; set; } = 1;
    public int TotalPages { get; set; }
    public int PageSize { get; set; } = 5;
    public int PageStart => (CurrentPage - 1) * PageSize + 1;
    public int PageEnd => Math.Min(CurrentPage * PageSize, TotalAccounts);

    // ── GET ─────────────────────────────────────────────────────
    public async Task OnGetAsync(int page = 1)
    {
        CurrentPage = page;

        Roles = await _db.Roles
            .Where(r => !r.IsDeleted)
            .OrderBy(r => r.RoleName)
            .ToListAsync();

        var query = _db.Users.Include(u => u.Role).AsQueryable();

        TotalAccounts = await query.CountAsync();
        ActiveCount = await query.CountAsync(u => u.IsActive);
        InactiveCount = TotalAccounts - ActiveCount;
        AdminCount = await query.CountAsync(u => u.Role!.RoleName == "Admin");
        TotalPages = (int)Math.Ceiling(TotalAccounts / (double)PageSize);

        Users = await query
            .OrderBy(u => u.FullName)
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();
    }

    // ── ADD ─────────────────────────────────────────────────────
    public async Task<IActionResult> OnPostAddAsync(
        string FullName,
        string Username,
        Guid RoleId,
        bool IsActive,
        string Password,
        string ConfirmPassword)
    {
        if (Password != ConfirmPassword)
        {
            AddError = "Passwords do not match.";
            await OnGetAsync();
            return Page();
        }

        if (await _db.Users.AnyAsync(u => u.Username == Username))
        {
            AddError = "Username already exists.";
            await OnGetAsync();
            return Page();
        }

        var user = new User
        {
            UserId = Guid.NewGuid(),
            FullName = FullName,
            Username = Username,
            RoleId = RoleId,
            IsActive = IsActive,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password), // see note below
            CreatedAt = DateTime.Now
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        return RedirectToPage();
    }

    // ── EDIT ────────────────────────────────────────────────────
    public async Task<IActionResult> OnPostEditAsync(
        Guid UserId,
        string FullName,
        string Username,
        Guid RoleId,
        bool IsActive,
        string? Password)
    {
        var user = await _db.Users.FindAsync(UserId);
        if (user == null) return NotFound();

        // Check username not taken by someone else
        if (await _db.Users.AnyAsync(u => u.Username == Username && u.UserId != UserId))
        {
            // Optionally handle duplicate username error here
            return RedirectToPage();
        }

        user.FullName = FullName;
        user.Username = Username;
        user.RoleId = RoleId;
        user.IsActive = IsActive;

        // Only update password if a new one was typed
        if (!string.IsNullOrWhiteSpace(Password))
        {
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password);
        }

        await _db.SaveChangesAsync();

        return RedirectToPage();
    }

    // ── DELETE ──────────────────────────────────────────────────
    public async Task<IActionResult> OnPostDeleteAsync(Guid UserId)
    {
        var user = await _db.Users.FindAsync(UserId);
        if (user != null)
        {
            _db.Users.Remove(user);
            await _db.SaveChangesAsync();
        }

        return RedirectToPage();
    }
}
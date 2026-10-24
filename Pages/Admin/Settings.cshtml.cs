using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OmsCafe.Data;
using OmsCafe.Models;
using OmsCafe.Services;

namespace OmsCafe.Pages.Admin
{
    public class SettingsModel : PageModel
    {
        private readonly AppDbContext _db;
        public SettingsModel(AppDbContext db) => _db = db;

        // ── Display ───────────────────────────────────────────────
        public string DisplayName { get; private set; } = "";
        public string RoleName { get; private set; } = "Administrator";
        public string MemberSince { get; private set; } = "";

        [TempData] public string? Message { get; set; }
        [TempData] public string? Error { get; set; }

        // ── Form models (nullable so MVC doesn't add implicit "required") ──
        [BindProperty] public ProfileInput Profile { get; set; } = new();
        [BindProperty] public PasswordInput Password { get; set; } = new();

        public class ProfileInput
        {
            public string? FullName { get; set; }
            public string? ContactNumber { get; set; }
            public string? Address { get; set; }
        }

        public class PasswordInput
        {
            public string? CurrentPassword { get; set; }
            public string? NewPassword { get; set; }
            public string? ConfirmPassword { get; set; }
        }

        // ── GET ───────────────────────────────────────────────────
        public async Task<IActionResult> OnGetAsync()
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToPage("/Login"); // adjust to your login page

            FillDisplay(user);
            Profile = new ProfileInput
            {
                FullName = user.FullName,
                ContactNumber = user.ContactNumber,
                Address = user.Address
            };
            return Page();
        }

        // ── Save profile ──────────────────────────────────────────
        public async Task<IActionResult> OnPostProfileAsync()
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToPage("/Login");

            ModelState.Clear();
            FillDisplay(user);

            var name = (Profile.FullName ?? "").Trim();
            var contact = (Profile.ContactNumber ?? "").Trim();
            var address = (Profile.Address ?? "").Trim();

            if (name.Length == 0)
                ModelState.AddModelError("Profile.FullName", "Enter your full name.");
            else if (name.Length > 100)
                ModelState.AddModelError("Profile.FullName", "Full name is too long (maximum 100 characters).");

            var contactProblem = ContactRules.GetContactProblem(contact);
            if (contactProblem != null)
                ModelState.AddModelError("Profile.ContactNumber", contactProblem);

            var addressProblem = ContactRules.GetAddressProblem(address);
            if (addressProblem != null)
                ModelState.AddModelError("Profile.Address", addressProblem);

            if (!ModelState.IsValid) return Page();

            user.FullName = name;
            user.ContactNumber = contact;
            user.Address = address;
            await _db.SaveChangesAsync();

            Message = "Your profile was updated.";
            return RedirectToPage();
        }

        // ── Change password ───────────────────────────────────────
        public async Task<IActionResult> OnPostPasswordAsync()
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return RedirectToPage("/Login");

            ModelState.Clear();
            FillDisplay(user);
            Profile = new ProfileInput { FullName = user.FullName, ContactNumber = user.ContactNumber, Address = user.Address };

            var current = Password.CurrentPassword ?? "";
            var next = Password.NewPassword ?? "";
            var confirm = Password.ConfirmPassword ?? "";

            if (current.Length == 0)
                ModelState.AddModelError("Password.CurrentPassword", "Enter your current password.");
            else if (!BCrypt.Net.BCrypt.Verify(current, user.PasswordHash))
                ModelState.AddModelError("Password.CurrentPassword", "Your current password is incorrect.");

            var problem = PasswordPolicy.GetProblem(next);
            if (problem != null)
                ModelState.AddModelError("Password.NewPassword", problem);
            else if (current.Length > 0 && next == current)
                ModelState.AddModelError("Password.NewPassword", "Your new password must be different from the current one.");
            else if (!string.IsNullOrWhiteSpace(user.Username) &&
                     next.Contains(user.Username, StringComparison.OrdinalIgnoreCase))
                ModelState.AddModelError("Password.NewPassword", "Your password must not contain your username.");

            if (confirm != next)
                ModelState.AddModelError("Password.ConfirmPassword", "Passwords do not match.");

            if (!ModelState.IsValid) return Page();

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(next);
            await _db.SaveChangesAsync();

            Message = "Your password was changed successfully.";
            return RedirectToPage();
        }

        // ── Helpers ───────────────────────────────────────────────
        private string? GetUsername() =>
            HttpContext.Session.GetString("Username") ?? User.Identity?.Name;

        private async Task<User?> GetCurrentUserAsync()
        {
            var username = GetUsername();
            if (string.IsNullOrWhiteSpace(username)) return null;
            return await _db.Users.FirstOrDefaultAsync(u => u.Username == username && !u.IsArchived);
        }

        private void FillDisplay(User user)
        {
            DisplayName = string.IsNullOrWhiteSpace(user.FullName) ? user.Username : user.FullName;
            RoleName = HttpContext.Session.GetString("Role") ?? "Administrator";
            MemberSince = user.CreatedAt.HasValue
                ? $"Member since {user.CreatedAt.Value:MMMM yyyy}"
                : "";
        }
    }
}
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OmsCafe.Services;

namespace OmsCafe.Pages
{
    public class LogoutModel : PageModel
    {
        private readonly ShiftService _shifts;
        public LogoutModel(ShiftService shifts) => _shifts = shifts;

        // Only cashiers have shifts. Admins and managers can always log out.
        private async Task<bool> HasOpenShiftAsync()
        {
            var role = HttpContext.Session.GetString("Role") ?? "";
            if (!role.Equals("cashier", StringComparison.OrdinalIgnoreCase)) return false;

            if (!Guid.TryParse(HttpContext.Session.GetString("UserId"), out var id)) return false;

            return await _shifts.GetOpenShiftAsync(id) != null;
        }

        // GET /Logout?handler=Status  (called by the popup before signing out)
        public async Task<IActionResult> OnGetStatusAsync()
            => new JsonResult(new { hasOpenShift = await HasOpenShiftAsync() });

        // POST /Logout  (the popup's hidden form)
        public Task<IActionResult> OnPostAsync() => SignOutAsync();

        // GET /Logout  (keeps plain <a href="/Logout"> links working)
        public Task<IActionResult> OnGetAsync() => SignOutAsync();

        private async Task<IActionResult> SignOutAsync()
        {
            // The server always re-checks, so the shift rule can't be skipped from the browser.
            if (await HasOpenShiftAsync())
            {
                TempData["LogoutBlocked"] = true;
                return RedirectToPage("/Cashier/CloseShift");
            }

            await HttpContext.Session.LoadAsync();
            HttpContext.Session.Clear();

            // Remove the session cookie so a brand-new session starts
            Response.Cookies.Delete(".AspNetCore.Session");

            // Stop the Back button from showing cached protected pages
            Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
            Response.Headers["Pragma"] = "no-cache";

            return RedirectToPage("/Login");
        }
    }
}
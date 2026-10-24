using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OmsCafe.Models;
using OmsCafe.Services;

namespace OmsCafe.Pages.Cashier
{
    public class OpenShiftRequest { public decimal OpeningFloat { get; set; } }

    public class CloseShiftModel : PageModel
    {
        private readonly ShiftService _shifts;
        public CloseShiftModel(ShiftService shifts) => _shifts = shifts;

        public Shift? OpenShift { get; private set; }
        public ShiftSummary? Summary { get; private set; }
        public ShiftClosingReport? LastReport { get; private set; }

        // Session-based auth (matches LoginModel). Cashiers only.
        private bool TryGetCashier(out Guid id, out string name)
        {
            name = HttpContext.Session.GetString("FullName") ?? "";
            var role = HttpContext.Session.GetString("Role") ?? "";
            return Guid.TryParse(HttpContext.Session.GetString("UserId"), out id)
                   && role.Equals("cashier", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<IActionResult> OnGetAsync()
        {
            if (!TryGetCashier(out var id, out _)) return RedirectToPage("/Login");

            OpenShift = await _shifts.GetOpenShiftAsync(id);
            if (OpenShift != null) Summary = await _shifts.GetSummaryAsync(OpenShift);
            else LastReport = await _shifts.GetLastReportAsync(id);
            return Page();
        }

        // Polled by the page so expected cash stays current while the cashier counts
        public async Task<IActionResult> OnGetSummaryAsync()
        {
            if (!TryGetCashier(out var id, out _)) return new UnauthorizedResult();
            var shift = await _shifts.GetOpenShiftAsync(id);
            if (shift == null) return new JsonResult(new { success = false });

            var s = await _shifts.GetSummaryAsync(shift);
            return new JsonResult(new
            {
                success = true,
                cashSales = s.CashSales,
                cashOrders = s.CashOrders,
                nonCashSales = s.NonCashSales,
                expectedCash = s.ExpectedCash
            });
        }

        public async Task<IActionResult> OnPostOpenAsync([FromBody] OpenShiftRequest body)
        {
            if (!TryGetCashier(out var id, out var name)) return new UnauthorizedResult();
            var (ok, message) = await _shifts.OpenAsync(id, name, body.OpeningFloat);
            return new JsonResult(new { success = ok, message });
        }

        public async Task<IActionResult> OnPostSubmitAsync([FromBody] SubmitShiftRequest body)
        {
            if (!TryGetCashier(out var id, out _)) return new UnauthorizedResult();
            var r = await _shifts.SubmitAsync(id, body);
            return new JsonResult(new { success = r.Success, message = r.Message, expectedCash = r.ExpectedCash });
        }
    }
}
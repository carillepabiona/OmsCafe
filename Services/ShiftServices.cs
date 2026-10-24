using Microsoft.EntityFrameworkCore;
using OmsCafe.Data;
using OmsCafe.Models;

namespace OmsCafe.Services
{
    public record ShiftSummary(decimal OpeningFloat, decimal CashSales, int CashOrders,
                               decimal NonCashSales, decimal ExpectedCash);

    public class SubmitShiftRequest
    {
        public int ShiftId { get; set; }
        public decimal ActualCash { get; set; }
        public decimal ExpectedShown { get; set; }   // what the cashier saw on screen
        public string? Explanation { get; set; }
    }

    public record SubmitResult(bool Success, string Message, decimal? ExpectedCash = null);

    public class ShiftService
    {
        public const decimal Tolerance = 0m;   // e.g. 20m => within ±₱20 counts as Balanced
        private readonly AppDbContext _db;
        public ShiftService(AppDbContext db) => _db = db;

        public Task<Shift?> GetOpenShiftAsync(Guid cashierId) =>
            _db.Shifts.FirstOrDefaultAsync(s => s.CashierId == cashierId && s.Status == ShiftStatus.Open);

        public Task<ShiftClosingReport?> GetLastReportAsync(Guid cashierId) =>
            _db.ShiftClosingReports.AsNoTracking()
               .Where(r => r.Shift.CashierId == cashierId)
               .OrderByDescending(r => r.SubmittedAt)
               .FirstOrDefaultAsync();

        public async Task<(bool ok, string message)> OpenAsync(Guid cashierId, string name, decimal openingFloat)
        {
            if (openingFloat < 0 || decimal.Round(openingFloat, 2) != openingFloat)
                return (false, "Enter a valid opening float (0 or more, max 2 decimals).");
            if (await GetOpenShiftAsync(cashierId) != null)
                return (false, "You already have an open shift.");

            _db.Shifts.Add(new Shift
            {
                CashierId = cashierId,
                CashierName = name,
                OpenedAt = PhTime.Now,
                OpeningFloat = openingFloat,
                Status = ShiftStatus.Open
            });

            try { await _db.SaveChangesAsync(); return (true, ""); }
            catch (DbUpdateException) { return (false, "You already have an open shift."); }
        }

        // Always computed server-side. Never trust a number from the browser.
        public async Task<ShiftSummary> GetSummaryAsync(Shift shift, DateTime? until = null)
        {
            var payments = _db.Payments.AsNoTracking()
                .Where(p => p.ShiftId == shift.Id && p.Order.Status != "cancelled");

            var cash = payments.Where(p => p.Order.PaymentMethod == "Cash");
            var cashSales = await cash.SumAsync(p => p.Amount);
            var cashOrders = await cash.CountAsync();
            var nonCash = await payments.Where(p => p.Order.PaymentMethod != "Cash").SumAsync(p => p.Amount);

            return new ShiftSummary(shift.OpeningFloat, cashSales, cashOrders, nonCash,
                                    shift.OpeningFloat + cashSales);
        }

        public async Task<SubmitResult> SubmitAsync(Guid cashierId, SubmitShiftRequest req)
        {
            var shift = await _db.Shifts.FirstOrDefaultAsync(s => s.Id == req.ShiftId && s.CashierId == cashierId);
            if (shift == null) return new(false, "Shift not found.");
            if (shift.Status != ShiftStatus.Open) return new(false, "This shift was already submitted.");

            if (req.ActualCash < 0 || decimal.Round(req.ActualCash, 2) != req.ActualCash)
                return new(false, "Enter a valid counted amount.");

            var now = PhTime.Now;
            var summary = await GetSummaryAsync(shift, now);

            // A sale landed after the cashier loaded the page: make them re-check
            if (summary.ExpectedCash != req.ExpectedShown)
                return new(false, "A new payment came in. Expected cash was updated, please review and submit again.",
                           summary.ExpectedCash);

            var variance = req.ActualCash - summary.ExpectedCash;
            var result = Math.Abs(variance) <= Tolerance ? "Balanced" : variance < 0 ? "Short" : "Over";
            var explanation = req.Explanation?.Trim();

            if (result != "Balanced" && string.IsNullOrWhiteSpace(explanation))
                return new(false, $"Please explain why the drawer is {result.ToLower()}.");

            shift.Status = ShiftStatus.PendingReview;
            shift.ClosedAt = now;

            _db.ShiftClosingReports.Add(new ShiftClosingReport
            {
                ShiftId = shift.Id,
                ExpectedCash = summary.ExpectedCash,
                ActualCash = req.ActualCash,
                Variance = variance,
                Result = result,
                Explanation = explanation,
                SubmittedAt = now
            });

            try { await _db.SaveChangesAsync(); }
            catch (DbUpdateConcurrencyException) { return new(false, "This shift was already submitted."); }

            return new(true, "Shift submitted for manager review.");
        }
    }
}
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using OmsCafe.Data;
using OmsCafe.Models;

namespace OmsCafe.Pages.Cashier
{
    public class PaymentModel : PageModel
    {
        private readonly AppDbContext _db;
        public PaymentModel(AppDbContext db) => _db = db;

        public List<Payment> Payments { get; set; } = new();

        [BindProperty(SupportsGet = true)]
        public string? SearchQuery { get; set; }

        [BindProperty(SupportsGet = true)]
        public string DateFilter { get; set; } = "today";

        // For "pick-week" mode — user selects a specific week
        [BindProperty(SupportsGet = true)]
        public string? WeekOf { get; set; }   // format: "yyyy-Www"  e.g. "2026-W23"

        // For "pick-month" mode — user selects a specific month
        [BindProperty(SupportsGet = true)]
        public string? MonthOf { get; set; }  // format: "yyyy-MM"   e.g. "2026-05"

        // Exposed so the view can show the resolved range label
        public string RangeLabel { get; private set; } = "";

        public async Task OnGetAsync()
        {
            var query = _db.Payments.AsQueryable();

            query = DateFilter switch
            {
                "today" => ApplyToday(query),
                "week" => ApplyThisWeek(query),
                "month" => ApplyThisMonth(query),
                "pick-week" => ApplyPickedWeek(query),
                "pick-month" => ApplyPickedMonth(query),
                _ => query   // "all"
            };

            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                var q = SearchQuery.ToLower();
                query = query.Where(p =>
                    p.OrderNumber.ToLower().Contains(q) ||
                    p.TableNumber.ToLower().Contains(q) ||
                    p.CustomerName.ToLower().Contains(q));
            }

            Payments = await query
                .OrderByDescending(p => p.PaidAt)
                .ToListAsync();
        }

        // ── Date range helpers ─────────────────────────────────────────────

        private IQueryable<Payment> ApplyToday(IQueryable<Payment> q)
        {
            var today = DateTime.Today;
            RangeLabel = today.ToString("MMMM dd, yyyy");
            return q.Where(p => p.PaidAt.Date == today);
        }

        private IQueryable<Payment> ApplyThisWeek(IQueryable<Payment> q)
        {
            var start = DateTime.Today.AddDays(-(int)DateTime.Today.DayOfWeek);
            var end = start.AddDays(7);
            RangeLabel = $"{start:MMM dd} – {end.AddDays(-1):MMM dd, yyyy}";
            return q.Where(p => p.PaidAt >= start && p.PaidAt < end);
        }

        private IQueryable<Payment> ApplyThisMonth(IQueryable<Payment> q)
        {
            var start = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            var end = start.AddMonths(1);
            RangeLabel = start.ToString("MMMM yyyy");
            return q.Where(p => p.PaidAt >= start && p.PaidAt < end);
        }

        private IQueryable<Payment> ApplyPickedWeek(IQueryable<Payment> q)
        {
            // Parse "yyyy-Www" — e.g. "2026-W23"
            if (!string.IsNullOrWhiteSpace(WeekOf) && WeekOf.Length == 8
                && int.TryParse(WeekOf[..4], out int year)
                && int.TryParse(WeekOf[6..], out int week))
            {
                // ISO week: find Monday of that week
                var jan4 = new DateTime(year, 1, 4); // Jan 4 is always in week 1
                var monday = jan4.AddDays(-((int)jan4.DayOfWeek - 1 + 7) % 7)
                                    .AddDays((week - 1) * 7);
                var sunday = monday.AddDays(7);
                RangeLabel = $"Week of {monday:MMM dd} – {sunday.AddDays(-1):MMM dd, yyyy}";
                return q.Where(p => p.PaidAt >= monday && p.PaidAt < sunday);
            }

            RangeLabel = "Selected week";
            return q;
        }

        private IQueryable<Payment> ApplyPickedMonth(IQueryable<Payment> q)
        {
            // Parse "yyyy-MM" — e.g. "2026-05"
            if (!string.IsNullOrWhiteSpace(MonthOf) && MonthOf.Length == 7
                && int.TryParse(MonthOf[..4], out int year)
                && int.TryParse(MonthOf[5..], out int month))
            {
                var start = new DateTime(year, month, 1);
                var end = start.AddMonths(1);
                RangeLabel = start.ToString("MMMM yyyy");
                return q.Where(p => p.PaidAt >= start && p.PaidAt < end);
            }

            RangeLabel = "Selected month";
            return q;
        }

        // ── Items JSON for receipt preview ─────────────────────────────────

        public string GetItemsJson(int orderId)
        {
            var items = _db.OrderItems
                .Where(i => i.OrderId == orderId)
                .Select(i => new {
                    quantity = i.Quantity,
                    name = i.Name,
                    price = i.Price,
                    customization = i.Customization ?? "",
                    specialInstructions = i.SpecialInstructions ?? ""
                })
                .ToList();

            return JsonSerializer.Serialize(items);
        }

        public async Task<IActionResult> OnPostPrintReceiptAsync(int orderId)
        {
            return RedirectToPage("/Cashier/Orders", new { handler = "PrintReceipt", orderId });
        }
    }
}
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OmsCafe.Data;
using OmsCafe.Models;

namespace OmsCafe.Pages.Cashier
{
    public class TransactionHistoryModel : PageModel
    {
        private readonly AppDbContext _db;
        public TransactionHistoryModel(AppDbContext db) => _db = db;

        // ── Bound properties ───────────────────────────────────────────────
        [BindProperty(SupportsGet = true)] public string? SearchQuery { get; set; }
        [BindProperty(SupportsGet = true)] public string DateFilter { get; set; } = "today";
        [BindProperty(SupportsGet = true)] public string? WeekOf { get; set; }
        [BindProperty(SupportsGet = true)] public string? MonthOf { get; set; }
        [BindProperty(SupportsGet = true)] public string TypeFilter { get; set; } = "all";
        [BindProperty(SupportsGet = true)] public string StatusFilter { get; set; } = "all";

        // ── Output ─────────────────────────────────────────────────────────
        public List<TransactionView> Transactions { get; set; } = new();
        public string RangeLabel { get; private set; } = "";
        public decimal TotalRevenue => Transactions.Sum(t => t.Total);
        public int TotalOrders => Transactions.Count;
        public int DineInCount => Transactions.Count(t => t.Type == "dine-in");
        public int TakeOutCount => Transactions.Count(t => t.Type == "take-out");

        // ── View model ─────────────────────────────────────────────────────
        public class TransactionView
        {
            public int Id { get; set; }
            public string OrderNumber { get; set; } = "";
            public string CustomerName { get; set; } = "";
            public string Type { get; set; } = "";
            public string TableNumber { get; set; } = "";
            public string Status { get; set; } = "";
            public string PaymentMethod { get; set; } = "";
            public decimal Total { get; set; }
            public DateTime CreatedAt { get; set; }
            public DateTime? ServedAt { get; set; }
            public string ElapsedTime { get; set; } = "";
            public List<ItemView> Items { get; set; } = new();
        }

        public class ItemView
        {
            public string Name { get; set; } = "";
            public int Quantity { get; set; }
            public decimal Price { get; set; }
            public string Customization { get; set; } = "";
            public string SpecialInstructions { get; set; } = "";
        }

        // ── GET ────────────────────────────────────────────────────────────
        public async Task OnGetAsync()
        {
            var query = _db.Orders
                .Include(o => o.Items)
                .Where(o => o.Status == "paid" ||
                            o.Status == "served" ||
                            o.Status == "completed")
                .AsQueryable();

            // Date filter
            query = DateFilter switch
            {
                "today" => ApplyToday(query),
                "week" => ApplyThisWeek(query),
                "month" => ApplyThisMonth(query),
                "pick-week" => ApplyPickedWeek(query),
                "pick-month" => ApplyPickedMonth(query),
                _ => query   // all
            };

            // Type filter
            if (TypeFilter != "all")
                query = query.Where(o => o.Type == TypeFilter);

            // Search
            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                var q = SearchQuery.ToLower();
                query = query.Where(o =>
                    o.OrderNumber.ToLower().Contains(q) ||
                    o.CustomerName.ToLower().Contains(q) ||
                    (o.TableNumber != null && o.TableNumber.ToLower().Contains(q)) ||
                    o.Items.Any(i => i.Name.ToLower().Contains(q)));
            }

            var orders = await query
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();

            Transactions = orders.Select(o =>
            {
                var elapsed = (o.ServedAt ?? DateTime.Now) - o.CreatedAt;
                return new TransactionView
                {
                    Id = o.Id,
                    OrderNumber = o.OrderNumber ?? "",
                    CustomerName = o.CustomerName ?? "",
                    Type = o.Type ?? "",
                    TableNumber = o.TableNumber ?? "",
                    Status = o.Status ?? "",
                    PaymentMethod = o.PaymentMethod ?? "",
                    Total = o.Total,
                    CreatedAt = o.CreatedAt,
                    ServedAt = o.ServedAt,
                    ElapsedTime = elapsed.TotalMinutes < 60
                        ? $"{(int)elapsed.TotalMinutes}m"
                        : $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m",
                    Items = o.Items.Select(i => new ItemView
                    {
                        Name = i.Name ?? "",
                        Quantity = i.Quantity,
                        Price = i.Price,
                        Customization = i.Customization ?? "",
                        SpecialInstructions = i.SpecialInstructions ?? ""
                    }).ToList()
                };
            }).ToList();
        }

        // ── Date helpers ───────────────────────────────────────────────────
        private IQueryable<Order> ApplyToday(IQueryable<Order> q)
        {
            var today = DateTime.Today;
            RangeLabel = today.ToString("MMMM dd, yyyy");
            return q.Where(o => o.CreatedAt.Date == today);
        }

        private IQueryable<Order> ApplyThisWeek(IQueryable<Order> q)
        {
            var start = DateTime.Today.AddDays(-(int)DateTime.Today.DayOfWeek);
            var end = start.AddDays(7);
            RangeLabel = $"{start:MMM dd} – {end.AddDays(-1):MMM dd, yyyy}";
            return q.Where(o => o.CreatedAt >= start && o.CreatedAt < end);
        }

        private IQueryable<Order> ApplyThisMonth(IQueryable<Order> q)
        {
            var start = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            var end = start.AddMonths(1);
            RangeLabel = start.ToString("MMMM yyyy");
            return q.Where(o => o.CreatedAt >= start && o.CreatedAt < end);
        }

        private IQueryable<Order> ApplyPickedWeek(IQueryable<Order> q)
        {
            if (!string.IsNullOrWhiteSpace(WeekOf) && WeekOf.Length == 8
                && int.TryParse(WeekOf[..4], out int year)
                && int.TryParse(WeekOf[6..], out int week))
            {
                var jan4 = new DateTime(year, 1, 4);
                var monday = jan4.AddDays(-((int)jan4.DayOfWeek - 1 + 7) % 7)
                                 .AddDays((week - 1) * 7);
                var sunday = monday.AddDays(7);
                RangeLabel = $"Week of {monday:MMM dd} – {sunday.AddDays(-1):MMM dd, yyyy}";
                return q.Where(o => o.CreatedAt >= monday && o.CreatedAt < sunday);
            }
            RangeLabel = "Selected week";
            return q;
        }

        private IQueryable<Order> ApplyPickedMonth(IQueryable<Order> q)
        {
            if (!string.IsNullOrWhiteSpace(MonthOf) && MonthOf.Length == 7
                && int.TryParse(MonthOf[..4], out int year)
                && int.TryParse(MonthOf[5..], out int month))
            {
                var start = new DateTime(year, month, 1);
                var end = start.AddMonths(1);
                RangeLabel = start.ToString("MMMM yyyy");
                return q.Where(o => o.CreatedAt >= start && o.CreatedAt < end);
            }
            RangeLabel = "Selected month";
            return q;
        }
    }
}
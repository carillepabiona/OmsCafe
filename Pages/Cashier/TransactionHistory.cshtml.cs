using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OmsCafe.Data;
using OmsCafe.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace OmsCafe.Pages.Cashier
{
    public class TransactionHistoryModel : PageModel
    {
        private readonly AppDbContext _db;

        public TransactionHistoryModel(AppDbContext db)
        {
            _db = db;
        }

        // Set to true ONLY if your code saves CreatedAt with PhTime.Now.
        private const bool CreatedAtIsUtc = false;

        private static DateTime Today => PhTime.Today;

        // =========================================================
        // FILTERS
        // =========================================================

        [BindProperty(SupportsGet = true)] public string? SearchQuery { get; set; }
        [BindProperty(SupportsGet = true)] public string DateFilter { get; set; } = "today";
        [BindProperty(SupportsGet = true)] public string? WeekOf { get; set; }
        [BindProperty(SupportsGet = true)] public string? MonthOf { get; set; }
        [BindProperty(SupportsGet = true)] public string TypeFilter { get; set; } = "all";
        [BindProperty(SupportsGet = true)] public string StatusFilter { get; set; } = "all";

        // =========================================================
        // OUTPUT / SUMMARY
        // =========================================================

        public List<TransactionView> Transactions { get; set; } = new();
        public string RangeLabel { get; private set; } = "";

        // Cancelled orders stay in the list but are not counted.
        public int TotalOrders => Transactions.Count(t => !IsCancelled(t.Status));

        // Revenue = orders that have a payment record and are not cancelled.
        public decimal TotalRevenue => Transactions
            .Where(t => t.IsPaid && !IsCancelled(t.Status))
            .Sum(t => t.Total);

        public int DineInCount => Transactions.Count(t =>
            !IsCancelled(t.Status) &&
            string.Equals(t.Type, "dine-in", StringComparison.OrdinalIgnoreCase));

        public int TakeOutCount => Transactions.Count(t =>
            !IsCancelled(t.Status) &&
            string.Equals(t.Type, "take-out", StringComparison.OrdinalIgnoreCase));

        private static bool IsCancelled(string? status) =>
            string.Equals(status, "cancelled", StringComparison.OrdinalIgnoreCase);

        // =========================================================
        // VIEW MODELS
        // =========================================================

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

            public bool IsPaid { get; set; }
            public DateTime? PaidAt { get; set; }

            public int? EstimatedMinutes { get; set; }
            public string SpecialInstructions { get; set; } = "";

            public string? DiscountType { get; set; }
            public decimal DiscountAmount { get; set; }
            public string? DiscountIdName { get; set; }
            public string? DiscountIdNumber { get; set; }
            public DateTime? DiscountVerifiedAt { get; set; }
            public string? DiscountVerifiedBy { get; set; }

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

            // Images are NOT embedded any more. The page asks for them
            // one by one (and only when the row is opened) via OnGetImageAsync.
            public string MenuItemId { get; set; } = "";
            public bool HasImage { get; set; }
        }

        // =========================================================
        // IMAGE ENDPOINT  (/Cashier/TransactionHistory?handler=Image&id=...)
        // =========================================================

        public async Task<IActionResult> OnGetImageAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return NotFound();

            var img = await _db.MenuItems
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(m => m.Id.ToString() == id)
                .Select(m => new { m.ImageBase64, m.ImageMimeType })
                .FirstOrDefaultAsync();

            if (img == null || string.IsNullOrEmpty(img.ImageBase64))
                return NotFound();

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(img.ImageBase64);
            }
            catch (FormatException)
            {
                return NotFound();
            }

            // The Admin/Cashier guard in Program.cs sets "no-store" for these
            // paths. Override it here so the browser can cache the image.
            Response.Headers.Remove("Pragma");
            Response.Headers["Cache-Control"] = "private, max-age=3600";

            return File(bytes,
                string.IsNullOrWhiteSpace(img.ImageMimeType) ? "image/png" : img.ImageMimeType);
        }

        // =========================================================
        // LOAD
        // =========================================================

        public async Task OnGetAsync()
        {
            // Defensive: if a duplicate query value ever arrives ("all,today"), use the first.
            DateFilter = (DateFilter ?? "today").Split(',')[0].Trim().ToLowerInvariant();

            IQueryable<Order> query = _db.Orders
                .AsNoTracking()
                .Include(o => o.Items);

            // Date filter
            query = DateFilter switch
            {
                "today" => ApplyToday(query),
                "week" => ApplyThisWeek(query),
                "month" => ApplyThisMonth(query),
                "pick-week" => ApplyPickedWeek(query),
                "pick-month" => ApplyPickedMonth(query),
                "all" => ApplyAllDates(query),
                _ => ApplyToday(query)
            };

            // Type filter
            if (!string.IsNullOrWhiteSpace(TypeFilter) && TypeFilter != "all")
            {
                var selectedType = TypeFilter.Trim().ToLowerInvariant();
                query = query.Where(o => o.Type != null && o.Type.ToLower() == selectedType);
            }

            // Status filter
            if (!string.IsNullOrWhiteSpace(StatusFilter) &&
                !string.Equals(StatusFilter, "all", StringComparison.OrdinalIgnoreCase))
            {
                var selectedStatus = StatusFilter.Trim().ToLowerInvariant();
                query = query.Where(o => o.Status != null && o.Status.ToLower() == selectedStatus);
            }

            // Search
            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                var search = SearchQuery.Trim().ToLowerInvariant();
                query = query.Where(o =>
                    (o.OrderNumber != null && o.OrderNumber.ToLower().Contains(search)) ||
                    (o.CustomerName != null && o.CustomerName.ToLower().Contains(search)) ||
                    (o.TableNumber != null && o.TableNumber.ToLower().Contains(search)) ||
                    o.Items.Any(i => i.Name != null && i.Name.ToLower().Contains(search)));
            }

            var orders = await query
                .OrderByDescending(o => o.CreatedAt)
                .ThenByDescending(o => o.Id)
                .ToListAsync();

            // Payments for these orders (PaidAt + IsPaid).
            // Assumes Payment.OrderId is the same type as Order.Id (int).
            var orderIds = orders.Select(o => o.Id).ToList();
            var payments = await _db.Payments
                .AsNoTracking()
                .Where(p => orderIds.Contains(p.OrderId))
                .Select(p => new { p.OrderId, p.PaidAt })
                .ToListAsync();

            var paidAtByOrder = payments
                .GroupBy(p => p.OrderId)
                .ToDictionary(g => g.Key, g => g.Max(p => p.PaidAt));

            // Which menu items have an image? We only fetch the IDs here,
            // NOT the base64 data (that was the slow part).
            var idsWithImage = (await _db.MenuItems
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(m => m.ImageBase64 != null && m.ImageBase64 != "")
                    .Select(m => m.Id)
                    .ToListAsync())
                .Select(id => id.ToString())
                .ToHashSet();

            // Map to view models
            Transactions = orders.Select(o =>
            {
                var endTime = o.ServedAt ?? PhTime.Now;
                var elapsed = endTime - o.CreatedAt;
                if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;

                var items = o.Items.Select(i =>
                {
                    var menuItemId = i.MenuItemId ?? "";
                    return new ItemView
                    {
                        Name = i.Name ?? "",
                        Quantity = i.Quantity,
                        Price = i.Price,
                        Customization = i.Customization ?? "",
                        SpecialInstructions = i.SpecialInstructions ?? "",
                        MenuItemId = menuItemId,
                        HasImage = menuItemId != "" && idsWithImage.Contains(menuItemId)
                    };
                }).ToList();

                var isPaid = paidAtByOrder.TryGetValue(o.Id, out var paidAt);

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

                    IsPaid = isPaid,
                    PaidAt = isPaid ? paidAt : null,

                    EstimatedMinutes = o.EstimatedMinutes,
                    SpecialInstructions = o.SpecialInstructions ?? "",

                    DiscountType = o.DiscountType,
                    DiscountAmount = o.DiscountAmount ?? 0m,
                    DiscountIdName = o.DiscountIdName,
                    DiscountIdNumber = o.DiscountIdNumber,
                    DiscountVerifiedAt = o.DiscountVerifiedAt,
                    DiscountVerifiedBy = o.DiscountVerifiedBy,

                    ElapsedTime = elapsed.TotalMinutes < 60
                        ? $"{(int)elapsed.TotalMinutes}m"
                        : $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m",

                    Items = items
                };
            }).ToList();
        }

        // =========================================================
        // DATE FILTER HELPERS
        // =========================================================

        private IQueryable<Order> ApplyToday(IQueryable<Order> query)
        {
            var today = Today;
            var tomorrow = today.AddDays(1);
            RangeLabel = today.ToString("MMMM dd, yyyy");
            return query.Where(o => o.CreatedAt >= today && o.CreatedAt < tomorrow);
        }

        private IQueryable<Order> ApplyThisWeek(IQueryable<Order> query)
        {
            // Sunday-start calendar week.
            var today = Today;
            var start = today.AddDays(-(int)today.DayOfWeek);
            var end = start.AddDays(7);
            RangeLabel = $"{start:MMM dd} – {end.AddDays(-1):MMM dd, yyyy}";
            return query.Where(o => o.CreatedAt >= start && o.CreatedAt < end);
        }

        private IQueryable<Order> ApplyThisMonth(IQueryable<Order> query)
        {
            var today = Today;
            var start = new DateTime(today.Year, today.Month, 1);
            var end = start.AddMonths(1);
            RangeLabel = start.ToString("MMMM yyyy");
            return query.Where(o => o.CreatedAt >= start && o.CreatedAt < end);
        }

        private IQueryable<Order> ApplyPickedWeek(IQueryable<Order> query)
        {
            // <input type="week"> sends e.g. "2026-W42"
            if (!string.IsNullOrWhiteSpace(WeekOf))
            {
                var parts = WeekOf.Split("-W");
                if (parts.Length == 2 &&
                    int.TryParse(parts[0], out var year) &&
                    int.TryParse(parts[1], out var week) &&
                    week >= 1 && week <= 53 && year >= 1 && year <= 9998)
                {
                    try
                    {
                        var monday = ISOWeek.ToDateTime(year, week, DayOfWeek.Monday);
                        var end = monday.AddDays(7);
                        RangeLabel = $"Week of {monday:MMM dd} – {end.AddDays(-1):MMM dd, yyyy}";
                        return query.Where(o => o.CreatedAt >= monday && o.CreatedAt < end);
                    }
                    catch (ArgumentOutOfRangeException) { /* fall through */ }
                }
            }

            RangeLabel = "All dates (invalid week selection)";
            return query;
        }

        private IQueryable<Order> ApplyPickedMonth(IQueryable<Order> query)
        {
            if (!string.IsNullOrWhiteSpace(MonthOf) &&
                DateTime.TryParseExact(MonthOf, "yyyy-MM", CultureInfo.InvariantCulture,
                                       DateTimeStyles.None, out var selected))
            {
                var start = new DateTime(selected.Year, selected.Month, 1);
                var end = start.AddMonths(1);
                RangeLabel = start.ToString("MMMM yyyy");
                return query.Where(o => o.CreatedAt >= start && o.CreatedAt < end);
            }

            RangeLabel = "All dates (invalid month selection)";
            return query;
        }

        private IQueryable<Order> ApplyAllDates(IQueryable<Order> query)
        {
            RangeLabel = "All Time";
            return query;
        }
    }
}
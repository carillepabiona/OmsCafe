
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

        // =========================================================
        // FILTERS
        // =========================================================

        [BindProperty(SupportsGet = true)]
        public string? SearchQuery { get; set; }

        [BindProperty(SupportsGet = true)]
        public string DateFilter { get; set; } = "today";

        [BindProperty(SupportsGet = true)]
        public string? WeekOf { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? MonthOf { get; set; }

        [BindProperty(SupportsGet = true)]
        public string TypeFilter { get; set; } = "all";

        [BindProperty(SupportsGet = true)]
        public string StatusFilter { get; set; } = "all";

        // =========================================================
        // OUTPUT / SUMMARY
        // =========================================================

        public List<TransactionView> Transactions { get; set; } = new();

        public string RangeLabel { get; private set; } = "";

        public int TotalOrders => Transactions.Count;

        // Only count orders whose status indicates payment was completed.
        // Unpaid orders remain visible but are excluded from this figure.
        public decimal TotalRevenue => Transactions
            .Where(t => IsPaidStatus(t.Status))
            .Sum(t => t.Total);

        public int DineInCount => Transactions.Count(t =>
            string.Equals(t.Type, "dine-in",
                StringComparison.OrdinalIgnoreCase));

        public int TakeOutCount => Transactions.Count(t =>
            string.Equals(t.Type, "take-out",
                StringComparison.OrdinalIgnoreCase));

        private static bool IsPaidStatus(string? status)
        {
            return string.Equals(status, "paid",
                       StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "served",
                       StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "completed",
                       StringComparison.OrdinalIgnoreCase);
        }

        // =========================================================
        // TRANSACTION VIEW MODEL
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

            // These fields are shown in the Razor page.
            // PaidAt must be populated from an actual payment record
            // once its schema and relationship are confirmed.
            public DateTime? PaidAt { get; set; }

            public int? EstimatedMinutes { get; set; }

            public string SpecialInstructions { get; set; } = "";

            // Discount information
            public string? DiscountType { get; set; }

            public decimal DiscountAmount { get; set; }

            public string? DiscountIdName { get; set; }

            public string? DiscountIdNumber { get; set; }

            public DateTime? DiscountVerifiedAt { get; set; }

            public string? DiscountVerifiedBy { get; set; }

            public string ElapsedTime { get; set; } = "";

            public List<ItemView> Items { get; set; } = new();
        }

        // =========================================================
        // ITEM VIEW MODEL
        // =========================================================

        public class ItemView
        {
            public string Name { get; set; } = "";

            public int Quantity { get; set; }

            public decimal Price { get; set; }

            public string Customization { get; set; } = "";

            public string SpecialInstructions { get; set; } = "";

            public string? ImageBase64 { get; set; }

            public string? ImageMimeType { get; set; }
        }

        // =========================================================
        // LOAD TRANSACTION HISTORY
        // =========================================================

        public async Task OnGetAsync()
        {
            // Include ALL recorded orders, including Waiting Payment.
            // Do not restrict the history to completed transactions.
            IQueryable<Order> query = _db.Orders
                .AsNoTracking()
                .Include(o => o.Items);

            // -----------------------------------------------------
            // DATE FILTER
            // -----------------------------------------------------

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

            // -----------------------------------------------------
            // ORDER TYPE FILTER
            // -----------------------------------------------------

            if (!string.IsNullOrWhiteSpace(TypeFilter) &&
                TypeFilter != "all")
            {
                var selectedType = TypeFilter.Trim().ToLowerInvariant();

                query = query.Where(o =>
                    o.Type != null &&
                    o.Type.ToLower() == selectedType);
            }

            // -----------------------------------------------------
            // ORDER STATUS FILTER
            // -----------------------------------------------------

            if (!string.IsNullOrWhiteSpace(StatusFilter) &&
                !string.Equals(StatusFilter, "all",
                    StringComparison.OrdinalIgnoreCase))
            {
                var selectedStatus = StatusFilter.Trim().ToLowerInvariant();

                query = query.Where(o =>
                    o.Status != null &&
                    o.Status.ToLower() == selectedStatus);
            }

            // -----------------------------------------------------
            // SEARCH BY ORDER, CUSTOMER, TABLE OR ITEM
            // -----------------------------------------------------

            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                var search = SearchQuery.Trim().ToLowerInvariant();

                query = query.Where(o =>
                    (o.OrderNumber != null &&
                     o.OrderNumber.ToLower().Contains(search))
                    ||
                    (o.CustomerName != null &&
                     o.CustomerName.ToLower().Contains(search))
                    ||
                    (o.TableNumber != null &&
                     o.TableNumber.ToLower().Contains(search))
                    ||
                    (o.Items != null &&
                     o.Items.Any(i =>
                         i.Name != null &&
                         i.Name.ToLower().Contains(search)))
                );
            }

            // -----------------------------------------------------
            // EXECUTE QUERY
            // -----------------------------------------------------

            var orders = await query
                .OrderByDescending(o => o.CreatedAt)
                .ThenByDescending(o => o.Id)
                .ToListAsync();

            // -----------------------------------------------------
            // MENU ITEM IMAGES
            // -----------------------------------------------------

            var menuItemImages = await _db.MenuItems
                .AsNoTracking()
                .Select(m => new
                {
                    m.Id,
                    m.ImageBase64,
                    m.ImageMimeType
                })
                .ToDictionaryAsync(
                    m => m.Id.ToString(),
                    m => new
                    {
                        m.ImageBase64,
                        m.ImageMimeType
                    });

            // -----------------------------------------------------
            // MAP DATABASE ORDERS TO VIEW MODELS
            // -----------------------------------------------------

            Transactions = orders.Select(o =>
            {
                var endTime = o.ServedAt ?? DateTime.Now;
                var elapsed = endTime - o.CreatedAt;

                // Prevent negative elapsed times from being displayed.
                if (elapsed < TimeSpan.Zero)
                {
                    elapsed = TimeSpan.Zero;
                }

                var items = o.Items.Select(i =>
                {
                    menuItemImages.TryGetValue(
                        i.MenuItemId ?? "",
                        out var image);

                    return new ItemView
                    {
                        Name = i.Name ?? "",
                        Quantity = i.Quantity,
                        Price = i.Price,
                        Customization = i.Customization ?? "",
                        SpecialInstructions =
                            i.SpecialInstructions ?? "",

                        ImageBase64 = image?.ImageBase64,
                        ImageMimeType = image?.ImageMimeType
                    };
                }).ToList();

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

                    // Do not substitute CreatedAt for PaidAt.
                    // A real payment timestamp needs to be mapped
                    // from the actual payment record.

                    PaidAt = null,

                    EstimatedMinutes = o.EstimatedMinutes,

                    SpecialInstructions =
                        o.SpecialInstructions ?? "",

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
            var today = DateTime.Today;
            var tomorrow = today.AddDays(1);

            RangeLabel = today.ToString("MMMM dd, yyyy");

            return query.Where(o =>
                o.CreatedAt >= today &&
                o.CreatedAt < tomorrow);
        }

        private IQueryable<Order> ApplyThisWeek(IQueryable<Order> query)
        {
            // Sunday-start calendar week.
            var today = DateTime.Today;
            var start = today.AddDays(-(int)today.DayOfWeek);
            var end = start.AddDays(7);

            RangeLabel =
                $"{start:MMM dd} – {end.AddDays(-1):MMM dd, yyyy}";

            return query.Where(o =>
                o.CreatedAt >= start &&
                o.CreatedAt < end);
        }

        private IQueryable<Order> ApplyThisMonth(IQueryable<Order> query)
        {
            var today = DateTime.Today;
            var start = new DateTime(today.Year, today.Month, 1);
            var end = start.AddMonths(1);

            RangeLabel = start.ToString("MMMM yyyy");

            return query.Where(o =>
                o.CreatedAt >= start &&
                o.CreatedAt < end);
        }

        private IQueryable<Order> ApplyPickedWeek(IQueryable<Order> query)
        {
            if (!string.IsNullOrWhiteSpace(WeekOf) &&
                DateTime.TryParseExact(
                    WeekOf + "-1",
                    "yyyy-'W'ww-d",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out _))
            {
                // The input type="week" normally supplies yyyy-Www.
                // Use ISOWeek for correct ISO week boundaries.
                var parts = WeekOf.Split("-W");

                if (parts.Length == 2 &&
                    int.TryParse(parts[0], out var year) &&
                    int.TryParse(parts[1], out var week) &&
                    week >= 1 && week <= 53 &&
                    year >= 1 && year <= 9998)
                {
                    try
                    {
                        var monday = ISOWeek.ToDateTime(
                            year, week, DayOfWeek.Monday);

                        var sunday = monday.AddDays(7);

                        RangeLabel =
                            $"Week of {monday:MMM dd} – {sunday.AddDays(-1):MMM dd, yyyy}";

                        return query.Where(o =>
                            o.CreatedAt >= monday &&
                            o.CreatedAt < sunday);
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        // Invalid ISO week; leave query unchanged.
                    }
                }
            }

            RangeLabel = "All dates (invalid week selection)";
            return query;
        }

        private IQueryable<Order> ApplyPickedMonth(IQueryable<Order> query)
        {
            if (!string.IsNullOrWhiteSpace(MonthOf) &&
                DateTime.TryParseExact(
                    MonthOf,
                    "yyyy-MM",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var selectedMonth))
            {
                var start = new DateTime(
                    selectedMonth.Year,
                    selectedMonth.Month,
                    1);

                var end = start.AddMonths(1);

                RangeLabel = start.ToString("MMMM yyyy");

                return query.Where(o =>
                    o.CreatedAt >= start &&
                    o.CreatedAt < end);
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
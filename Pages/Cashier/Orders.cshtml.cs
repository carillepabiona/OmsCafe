using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OmsCafe.Data;
using OmsCafe.Models;
using Microsoft.EntityFrameworkCore;

namespace OmsCafe.Pages.Cashier
{
    public class OrdersModel : PageModel
    {
        private readonly AppDbContext _db;
        public OrdersModel(AppDbContext db) => _db = db;

        [BindProperty(SupportsGet = true)]
        public string SearchQuery { get; set; } = "";

        [BindProperty(SupportsGet = true)]
        public string SelectedFilter { get; set; } = "all";

        [BindProperty(SupportsGet = true)]
        public string SelectedTab { get; set; } = "all";

        // ── View Models ────────────────────────────────────────────────────
        public class OrderItemView
        {
            public int Quantity { get; set; }
            public string Name { get; set; } = "";
            public decimal Price { get; set; }
            public string Customization { get; set; } = "";
            public string SpecialInstructions { get; set; } = "";
        }

        public class OrderView
        {
            public int Id { get; set; }
            public string OrderNumber { get; set; } = "";
            public string CustomerName { get; set; } = "";
            public string Type { get; set; } = "";
            public required string Table { get; set; }
            public string Status { get; set; } = "";
            public string PaymentMethod { get; set; } = "";
            public List<OrderItemView> Items { get; set; } = new();
            public decimal Total { get; set; }
            public string ElapsedTime { get; set; } = "";
            public string SpecialInstructions { get; set; } = "";
            public int ItemCount => Items.Sum(i => i.Quantity);
        }

        public List<OrderView> AllOrders { get; private set; } = new();
        public List<OrderView> OrdersFiltered => FilterAndSortOrders();

        // ── GET ────────────────────────────────────────────────────────────
        public void OnGet() => LoadOrders();

        // ── POST: update status ────────────────────────────────────────────
        public IActionResult OnPostUpdateStatus(int orderId, string newStatus,
                                                string selectedTab, string selectedFilter)
        {
            var order = _db.Orders
                .Include(o => o.Items)
                .FirstOrDefault(o => o.Id == orderId);

            if (order != null)
            {
                // ── Record payment when cashier marks as paid ──────────────
                if (newStatus == "pending" && order.Status == "awaiting_payment")
                {
                    var payment = new Payment
                    {
                        OrderId = order.Id,
                        OrderNumber = order.OrderNumber ?? "",
                        TableNumber = order.TableNumber ?? "",
                        CustomerName = order.CustomerName ?? "",
                        Type = order.Type ?? "",
                        Amount = order.Total,
                        PaidAt = DateTime.Now
                    };
                    _db.Payments.Add(payment);
                }

                order.Status = newStatus;
                _db.SaveChanges();
            }

            return RedirectToPage(new { selectedTab, selectedFilter, searchQuery = SearchQuery });
        }

        // ── POST: print receipt ────────────────────────────────────────────
        public IActionResult OnPostPrintReceipt(int orderId)
        {
            return RedirectToPage(new
            {
                selectedTab = SelectedTab,
                selectedFilter = SelectedFilter,
                searchQuery = SearchQuery
            });
        }

        // ── Load from DB via EF ────────────────────────────────────────────
        private void LoadOrders()
        {
            var dbOrders = _db.Orders
                .Include(o => o.Items)
                .OrderByDescending(o => o.CreatedAt)
                .ToList();

            AllOrders = dbOrders.Select(o =>
            {
                var elapsed = DateTime.UtcNow - o.CreatedAt;
                var elapsedStr = elapsed.TotalMinutes < 60
                    ? $"{(int)elapsed.TotalMinutes} mins"
                    : $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m";

                return new OrderView
                {
                    Id = o.Id,
                    OrderNumber = o.OrderNumber ?? "",
                    CustomerName = o.CustomerName ?? "",
                    Type = o.Type ?? "",
                    Table = o.TableNumber ?? "",
                    Status = o.Status ?? "",
                    PaymentMethod = o.PaymentMethod ?? "",
                    Total = o.Total,
                    ElapsedTime = elapsedStr,
                    SpecialInstructions = o.SpecialInstructions ?? "",
                    Items = o.Items.Select(i => new OrderItemView
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

        // ── Filter logic ───────────────────────────────────────────────────
        private List<OrderView> FilterAndSortOrders()
        {
            var filtered = AllOrders.AsEnumerable();

            filtered = SelectedTab switch
            {
                "dine-in" => filtered.Where(o => o.Type == "dine-in"
                                   && o.Status != "paid" && o.Status != "served"),
                "take-out" => filtered.Where(o => o.Type == "take-out"
                                   && o.Status != "paid" && o.Status != "served"),
                "completed" => filtered.Where(o => o.Status == "paid" || o.Status == "served"),
                "all" => filtered.Where(o => o.Status != "paid" && o.Status != "served"),
                _ => filtered
            };

            if (SelectedFilter != "all")
                filtered = filtered.Where(o => o.Status == SelectedFilter);

            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                var q = SearchQuery.ToLower();
                filtered = filtered.Where(o =>
                    o.OrderNumber.ToLower().Contains(q) ||
                    o.CustomerName.ToLower().Contains(q) ||
                    (!string.IsNullOrEmpty(o.Table) && o.Table.ToLower().Contains(q)) ||
                    o.Items.Any(i => i.Name.ToLower().Contains(q))
                );
            }

            return filtered.ToList();
        }
    }
}
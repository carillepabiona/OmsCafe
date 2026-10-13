
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OmsCafe.Data;
using OmsCafe.Models;

namespace OmsCafe.Pages.Cashier
{
    public class OrdersModel : PageModel
    {
        private readonly AppDbContext _db;

        public OrdersModel(AppDbContext db)
        {
            _db = db;
        }

        // PWD / Senior: VAT-exempt prices + 20% discount
        private const decimal VatRate = 0.12m;
        private const decimal PwdSeniorRate = 0.20m;

        [BindProperty(SupportsGet = true)]
        public string SearchQuery { get; set; } = "";

        [BindProperty(SupportsGet = true)]
        public string SelectedFilter { get; set; } = "all";

        [BindProperty(SupportsGet = true)]
        public string SelectedTab { get; set; } = "all";

        // ── View Models ──────────────────────────────────────────────

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

            // Final order total, including applicable discounts
            public decimal Total { get; set; }

            public string ElapsedTime { get; set; } = "";
            public string SpecialInstructions { get; set; } = "";

            // ── Discount information ────────────────────────────────
            public string? DiscountRequest { get; set; }
            public string? DiscountType { get; set; }
            public decimal DiscountAmount { get; set; }
            public string? DiscountIdNumber { get; set; }
            public string? DiscountIdName { get; set; }
            public string? DiscountVerifiedBy { get; set; }

            public int ItemCount => Items.Sum(i => i.Quantity);
        }

        public List<OrderView> AllOrders { get; private set; } = new();

        public List<OrderView> OrdersFiltered => FilterAndSortOrders();

        // ── Tab / status rules ───────────────────────────────────────

        private static readonly string[] ValidTabs =
        {
            "all", "dine-in", "take-out", "completed"
        };

        private static readonly string[] ValidFilter =
        {
            "all", "Waiting Payment", "pending", "preparing", "ready"
        };

        public static bool IsCompleted(string status) =>
            status == "paid" || status == "served";

        private static bool MatchesTab(OrderView order, string tab)
        {
            if (order.Status == "cancelled")
                return false;

            return tab switch
            {
                "completed" => IsCompleted(order.Status),
                "dine-in" => !IsCompleted(order.Status)
                              && order.Type == "dine-in",
                "take-out" => !IsCompleted(order.Status)
                              && order.Type == "take-out",
                _ => !IsCompleted(order.Status)
            };
        }

        // Badge count for a tab, ignoring status filter and search
        public int CountFor(string tab) =>
            AllOrders.Count(o => MatchesTab(o, tab));

        // ── GET ──────────────────────────────────────────────────────

        public void OnGet()
        {
            if (!ValidTabs.Contains(SelectedTab))
                SelectedTab = "all";

            if (!ValidFilter.Contains(SelectedFilter))
                SelectedFilter = "all";

            SearchQuery ??= "";

            LoadOrders();
        }

        // ── POST: update order status and record payment ─────────────

        public IActionResult OnPostUpdateStatus(
            int orderId,
            string newStatus,
            string selectedTab,
            string selectedFilter,
            decimal? amountTendered,
            decimal? changeAmount)
        {
            var order = _db.Orders
                .FirstOrDefault(o => o.Id == orderId);

            if (order == null)
                return NotFound();

            // Record payment when cashier confirms payment.
            if (newStatus == "pending"
                && order.Status == "Waiting Payment")
            {
                // Prevent creating a second payment record for this order.
                var existingPayment = _db.Payments
                    .FirstOrDefault(p => p.OrderId == order.Id);

                if (existingPayment != null)
                {
                    // Payment already exists; continue receipt workflow.
                    order.Status = "printing";
                }
                else
                {
                    // Validate cash received on the server.
                    if (!amountTendered.HasValue
                        || amountTendered.Value < 0m)
                    {
                        return BadRequest(
                            "Invalid cash received amount.");
                    }

                    decimal cashReceived =
                        Math.Round(amountTendered.Value, 2);

                    decimal orderTotal =
                        Math.Round(order.Total, 2);

                    if (cashReceived < orderTotal)
                    {
                        return BadRequest(
                            "Cash received is less than the order total.");
                    }

                    // Always calculate change using the database total.
                    decimal calculatedChange =
                        Math.Round(cashReceived - orderTotal, 2);

                    var payment = new Payment
                    {
                        OrderId = order.Id,
                        OrderNumber = order.OrderNumber ?? "",
                        TableNumber = order.TableNumber ?? "",
                        CustomerName = order.CustomerName ?? "",
                        Type = order.Type ?? "",
                        Amount = orderTotal,
                        PaidAt = DateTime.Now,

                        // New saved payment values
                        CashReceived = cashReceived,
                        ChangeGiven = calculatedChange
                    };

                    _db.Payments.Add(payment);

                    // Payment is recorded; receipt animation begins.
                    order.Status = "printing";
                }
            }
            else
            {
                // Preserve the normal status update behavior.
                order.Status = newStatus;
            }

            _db.SaveChanges();

            return RedirectToPage(new
            {
                selectedTab,
                selectedFilter,
                searchQuery = SearchQuery
            });
        }

        // ── POST: apply PWD / Senior discount ────────────────────────

        public IActionResult OnPostApplyDiscount(
            int orderId,
            string discountType,
            string idNumber,
            string idName,
            string selectedTab,
            string selectedFilter)
        {
            var order = _db.Orders
                .FirstOrDefault(o => o.Id == orderId);

            if (order == null)
                return NotFound();

            bool validType =
                discountType == "PWD" || discountType == "Senior";

            bool hasId =
                !string.IsNullOrWhiteSpace(idNumber)
                && !string.IsNullOrWhiteSpace(idName);

            if (order.Status == "Waiting Payment"
                && validType
                && hasId)
            {
                // Restore the gross amount before recalculating discount.
                decimal gross =
                    order.Total + (order.DiscountAmount ?? 0m);

                decimal vatExclusive =
                    Math.Round(gross / (1 + VatRate), 2);

                decimal final =
                    Math.Round(
                        vatExclusive * (1 - PwdSeniorRate), 2);

                order.Total = final;
                order.DiscountAmount = gross - final;
                order.DiscountType = discountType;
                order.DiscountIdNumber = idNumber.Trim();
                order.DiscountIdName = idName.Trim();
                order.DiscountVerifiedAt = DateTime.Now;
                order.DiscountVerifiedBy =
                    User.Identity?.Name ?? "cashier";

                _db.SaveChanges();
            }

            return RedirectToPage(new
            {
                selectedTab,
                selectedFilter,
                searchQuery = SearchQuery
            });
        }

        // ── POST: remove discount ────────────────────────────────────

        public IActionResult OnPostRemoveDiscount(
            int orderId,
            string selectedTab,
            string selectedFilter)
        {
            var order = _db.Orders
                .FirstOrDefault(o => o.Id == orderId);

            if (order == null)
                return NotFound();

            if (order.Status == "Waiting Payment"
                && !string.IsNullOrEmpty(order.DiscountType))
            {
                order.Total += order.DiscountAmount ?? 0m;

                order.DiscountAmount = null;
                order.DiscountType = null;
                order.DiscountIdNumber = null;
                order.DiscountIdName = null;
                order.DiscountVerifiedAt = null;
                order.DiscountVerifiedBy = null;

                _db.SaveChanges();
            }

            return RedirectToPage(new
            {
                selectedTab,
                selectedFilter,
                searchQuery = SearchQuery
            });
        }

        // ── POST: receipt finished printing ──────────────────────────

        public IActionResult OnPostCompletePrint(int orderId)
        {
            var order = _db.Orders
                .FirstOrDefault(o => o.Id == orderId);

            // Only advance an order that is currently printing.
            if (order != null && order.Status == "printing")
            {
                order.Status = "pending";
                _db.SaveChanges();
            }

            return new JsonResult(new
            {
                ok = order != null,
                status = order?.Status
            });
        }

        // ── POST: manual receipt reprint ─────────────────────────────

        public IActionResult OnPostPrintReceipt(
            int orderId)
        {
            return RedirectToPage(new
            {
                selectedTab = SelectedTab,
                selectedFilter = SelectedFilter,
                searchQuery = SearchQuery
            });
        }

        // ── Load orders from database ────────────────────────────────

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

                    DiscountRequest = o.DiscountRequest ?? "None",
                    DiscountType = o.DiscountType,
                    DiscountAmount = o.DiscountAmount ?? 0m,
                    DiscountIdNumber = o.DiscountIdNumber,
                    DiscountIdName = o.DiscountIdName,
                    DiscountVerifiedBy = o.DiscountVerifiedBy,

                    Items = o.Items.Select(i => new OrderItemView
                    {
                        Name = i.Name ?? "",
                        Quantity = i.Quantity,
                        Price = i.Price,
                        Customization = i.Customization ?? "",
                        SpecialInstructions =
                            i.SpecialInstructions ?? ""
                    }).ToList()
                };
            }).ToList();
        }

        // ── Filter and sort orders ───────────────────────────────────

        private List<OrderView> FilterAndSortOrders()
        {
            var filtered = AllOrders
                .Where(o => MatchesTab(o, SelectedTab));

            // Status filter applies only to active tabs.
            if (SelectedTab != "completed"
                && SelectedFilter != "all")
            {
                filtered = filtered
                    .Where(o => o.Status == SelectedFilter);
            }

            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                var q = SearchQuery.ToLower();

                filtered = filtered.Where(o =>
                    o.OrderNumber.ToLower().Contains(q)
                    || o.CustomerName.ToLower().Contains(q)
                    || (!string.IsNullOrEmpty(o.Table)
                        && o.Table.ToLower().Contains(q))
                    || o.Items.Any(i =>
                        i.Name.ToLower().Contains(q)));
            }

            return filtered.ToList();
        }
    }
}
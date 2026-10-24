// ════════════════════════════════════════════════════════════════════
// Pages/Api/KdsController.cs  — JSON API consumed by the KDS + Cashier pages
// ════════════════════════════════════════════════════════════════════
using Microsoft.AspNetCore.Mvc;
using OmsCafe.Data;
using Microsoft.EntityFrameworkCore;

namespace OmsCafe.Pages.Api
{
    [ApiController]
    [Route("api/kds")]
    [IgnoreAntiforgeryToken]
    public class KdsController : ControllerBase
    {
        private readonly AppDbContext _db;
        public KdsController(AppDbContext db) => _db = db;

        // "Waiting Payment" / "waiting_payment" / "awaiting-payment" -> "waiting payment"
        private static string Norm(string? s) =>
            (s ?? "").Replace('_', ' ').Replace('-', ' ').Trim().ToLowerInvariant();

        // ── GET /api/kds/orders ─────────────────────────────────────────────
        [HttpGet("orders")]
        public IActionResult GetOrders()
        {
            var orders = _db.Orders
                .Include(o => o.Items)
                .Where(o => o.Status == "pending"
                         || o.Status == "preparing"
                         || o.Status == "ready")
                .OrderBy(o => o.CreatedAt)
                .Select(o => new
                {
                    id = o.Id,
                    orderNumber = o.OrderNumber,
                    customerName = o.CustomerName,
                    type = o.Type,
                    tableNumber = o.TableNumber,
                    status = o.Status,
                    createdAt = o.CreatedAt,
                    items = o.Items.Select(i => new
                    {
                        name = i.Name,
                        quantity = i.Quantity,
                        customization = i.Customization,
                        specialInstructions = i.SpecialInstructions
                    })
                })
                .ToList();

            return Ok(orders);
        }

        // ── POST /api/kds/status ────────────────────────────────────────────
        // Waiting Payment → pending → preparing → ready → served
        [HttpPost("status")]
        public async Task<IActionResult> UpdateStatus([FromBody] KdsStatusDto dto)
        {
            var order = await _db.Orders.FindAsync(dto.OrderId);
            if (order == null)
                return NotFound(new { error = "Order not found" });

            // Keys are normalized, so any spelling of "waiting payment" is accepted.
            var allowed = new Dictionary<string, string[]>
            {
                { "waiting payment",  new[] { "pending",   "cancelled" } },
                { "awaiting payment", new[] { "pending",   "cancelled" } },
                { "unpaid",           new[] { "pending",   "cancelled" } },
                { "pending",          new[] { "preparing", "cancelled" } },
                { "preparing",        new[] { "ready"                  } },
                { "ready",            new[] { "served", "paid"         } }   // cashier page posts "paid"
            };

            var newStatus = Norm(dto.NewStatus);

            if (!allowed.TryGetValue(Norm(order.Status), out var validNext) || !validNext.Contains(newStatus))
                return BadRequest(new { error = $"Cannot transition from {order.Status} to {dto.NewStatus}" });

            order.Status = newStatus;
            await _db.SaveChangesAsync();

            return Ok(new { success = true, newStatus = order.Status });
        }

        // ── GET /api/kds/orders-all ─────────────────────────────────────────
        // All non-cancelled/completed orders for the cashier sync.
        [HttpGet("orders-all")]
        public IActionResult GetAllOrders()
        {
            var orders = _db.Orders
                .Include(o => o.Items)
                .Where(o => o.Status != "completed" && o.Status != "cancelled")
                .OrderBy(o => o.CreatedAt)
                .Select(o => new
                {
                    id = o.Id,
                    orderNumber = o.OrderNumber,
                    customerName = o.CustomerName,
                    type = o.Type,
                    table = o.TableNumber,
                    status = o.Status,
                    total = o.Total,
                    itemCount = o.Items.Count,
                    elapsedTime = (int)(PhTime.Now - o.CreatedAt).TotalMinutes + " mins",

                    // ── Discount ──
                    discountRequest = o.DiscountRequest ?? "None",
                    discountType = o.DiscountType,
                    discountAmount = o.DiscountAmount ?? 0m,
                    discountIdNumber = o.DiscountIdNumber,
                    discountIdName = o.DiscountIdName,

                    items = o.Items.Select(i => new
                    {
                        name = i.Name,
                        quantity = i.Quantity
                    })
                })
                .ToList();

            return Ok(orders);
        }
    }

    public class KdsStatusDto
    {
        public int OrderId { get; set; }
        public string NewStatus { get; set; } = "";
    }
}
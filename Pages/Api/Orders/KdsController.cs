// ════════════════════════════════════════════════════════════════════
// Pages/Api/KdsController.cs  — JSON API consumed by the KDS page
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

        // ── GET /api/kds/orders ─────────────────────────────────────────────
        // Returns pending / preparing / ready orders for the KDS display
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
        // Advances an order through the workflow:
        //   awaiting_payment → pending → preparing → ready → served
        [HttpPost("status")]
        public async Task<IActionResult> UpdateStatus([FromBody] KdsStatusDto dto)
        {
            var order = await _db.Orders.FindAsync(dto.OrderId);
            if (order == null)
                return NotFound(new { error = "Order not found" });

            var allowed = new Dictionary<string, string[]>
            {
                { "awaiting_payment", new[] { "pending",   "cancelled" } },
                { "pending",          new[] { "preparing", "cancelled" } },
                { "preparing",        new[] { "ready"                  } },
                { "ready",            new[] { "served"                 } }   // ✅ "served" not "paid"
            };

            if (!allowed.TryGetValue(order.Status, out var validNext) || !validNext.Contains(dto.NewStatus))
                return BadRequest(new { error = $"Cannot transition from {order.Status} to {dto.NewStatus}" });

            order.Status = dto.NewStatus;
            await _db.SaveChangesAsync();

            return Ok(new { success = true, newStatus = order.Status });
        }

        // ── GET /api/kds/orders-all ─────────────────────────────────────────
        // Returns ALL non-cancelled/completed orders for the cashier sync.
        // Includes full item/customer details so the cashier page can inject
        // new cards that arrived after the page first loaded.
        [HttpGet("orders-all")]
        public IActionResult GetAllOrders()
        {
            var orders = _db.Orders
                .Include(o => o.Items)                                          // ✅ include items
                .Where(o => o.Status != "completed" && o.Status != "cancelled")
                .OrderBy(o => o.CreatedAt)
                .Select(o => new
                {
                    id = o.Id,
                    orderNumber = o.OrderNumber,
                    customerName = o.CustomerName,                              // ✅ added
                    type = o.Type,                                      // ✅ added
                    table = o.TableNumber,                               // ✅ added
                    status = o.Status,
                    total = o.Total,                                     // ✅ added
                    itemCount = o.Items.Count,                               // ✅ added
                    elapsedTime = (int)(DateTime.UtcNow - o.CreatedAt).TotalMinutes + " mins",
                    items = o.Items.Select(i => new                      // ✅ added
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

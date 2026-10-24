using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OmsCafe.Data;
using OmsCafe.Models;

namespace OmsCafe.Pages.Api
{
    // The cashier is identified by the session (UserId), same as the Orders page.
    // Every endpoint below checks it, so [Authorize] is not needed here.
    [ApiController]
    [Route("api/pos")]
    [IgnoreAntiforgeryToken]
    public class PosOrdersController : ControllerBase
    {
        // The Orders page and kitchen compare statuses in LOWERCASE
        // ("pending", "preparing", "ready", "paid"), so this must be lowercase too.
        // "preparing" = paid at the counter, goes straight to the kitchen.
        // Use "pending" instead if you want the cashier to press "Accept Order" first.
        private const string StatusAfterPayment = "preparing";

        // PWD / Senior discount. Keep in sync with the constants in Pos.cshtml.
        // false = flat 20% off the listed price. true = remove 12% VAT first, then 20% off.
        private const bool ApplyVatExemption = false;
        private const decimal VatRate = 0.12m;
        private const decimal DiscountRate = 0.20m;

        private readonly AppDbContext _db;
        public PosOrdersController(AppDbContext db) => _db = db;

        private bool IsLoggedIn() =>
            Guid.TryParse(HttpContext.Session.GetString("UserId"), out _);

        // The logged-in cashier's open shift (same rule the Orders page uses)
        private async Task<Shift?> GetOpenShiftAsync()
        {
            if (!Guid.TryParse(HttpContext.Session.GetString("UserId"), out var cashierId))
                return null;

            return await _db.Shifts.FirstOrDefaultAsync(s =>
                s.CashierId == cashierId && s.Status == ShiftStatus.Open);
        }

        // GET api/pos/orders/recent : today's take-out orders, so the cashier can pick one to add onto
        [HttpGet("orders/recent")]
        public async Task<IActionResult> Recent()
        {
            if (!IsLoggedIn()) return Unauthorized(new { error = "You are not logged in." });
            var today = PhTime.Now.Date;
            var list = await _db.Orders.AsNoTracking()
                .Where(o => o.CreatedAt >= today && o.Type == "take-out")
                .OrderByDescending(o => o.Id).Take(15)
                .Select(o => new { o.OrderNumber, o.CustomerName, o.Total, o.Status })
                .ToListAsync();
            return Ok(list);
        }

        // GET api/pos/orders/ORD-00012
        [HttpGet("orders/{number}")]
        public async Task<IActionResult> Lookup(string number)
        {
            if (!IsLoggedIn()) return Unauthorized(new { error = "You are not logged in." });
            var o = await _db.Orders.AsNoTracking().Include(x => x.Items)
                .FirstOrDefaultAsync(x => x.OrderNumber == number.Trim().ToUpper());
            if (o == null) return NotFound(new { error = "Order not found" });

            return Ok(new
            {
                o.OrderNumber,
                o.CustomerName,
                o.Type,
                o.Status,
                o.Total,
                items = o.Items.Select(i => new { i.Name, i.Quantity, i.Price, i.Customization })
            });
        }

        // POST api/pos/orders : cashier rings up an order (new, or an add-on to an existing one)
        [HttpPost("orders")]
        public async Task<IActionResult> Create([FromBody] PosOrderDto dto)
        {
            try
            {
                return await CreateCore(dto);
            }
            catch (Exception ex)
            {
                // Shows the real reason on the POS screen instead of a bare "HTTP 500".
                return StatusCode(500, new { error = "Server error: " + ex.GetBaseException().Message });
            }
        }

        private async Task<IActionResult> CreateCore(PosOrderDto dto)
        {
            if (dto?.Items == null || dto.Items.Count == 0 || dto.Items.Any(i => i.Quantity < 1))
                return BadRequest(new { error = "Order is empty" });

            // Cash payments must belong to an open shift, same as on the Orders page
            if (!IsLoggedIn())
                return Unauthorized(new { error = "You are not logged in. Please log in again." });

            var shift = await GetOpenShiftAsync();
            if (shift == null)
                return BadRequest(new { error = "No open shift. Go to Close Shift and open your shift before taking payments." });

            var customer = string.IsNullOrWhiteSpace(dto.CustomerName) ? "Walk-in" : dto.CustomerName.Trim();
            var type = "take-out";
            string note = "";

            if (!string.IsNullOrWhiteSpace(dto.AddOnToOrderNumber))
            {
                var parent = await _db.Orders.AsNoTracking()
                    .FirstOrDefaultAsync(o => o.OrderNumber == dto.AddOnToOrderNumber.Trim().ToUpper());
                if (parent == null) return NotFound(new { error = "The original order was not found" });

                customer = parent.CustomerName;
                type = parent.Type;
                note = $"Add-on to {parent.OrderNumber}";   // the link; kitchen sees it in the order notes
            }

            var subtotal = dto.Items.Sum(i => i.UnitPrice * i.Quantity);

            // Only PWD / Senior are accepted; anything else means no discount.
            var discType = dto.DiscountType?.Trim() switch { "PWD" => "PWD", "Senior" => "Senior", _ => null };
            decimal discount = 0m;
            if (discType != null)
            {
                if (string.IsNullOrWhiteSpace(dto.DiscountIdNumber) || string.IsNullOrWhiteSpace(dto.DiscountIdName))
                    return BadRequest(new { error = "ID number and name are required for a PWD / Senior discount" });

                var basis = ApplyVatExemption ? subtotal / (1 + VatRate) : subtotal;
                discount = subtotal - Math.Round(basis * (1 - DiscountRate), 2, MidpointRounding.AwayFromZero);
            }
            var total = subtotal - discount;   // final amount the customer pays

            if (dto.AmountTendered < total)
                return BadRequest(new { error = "Cash received is less than the total" });

            var order = new Order
            {
                OrderNumber = "TMP-" + Guid.NewGuid().ToString("N")[..10],
                CustomerName = customer,
                Type = type,
                PaymentMethod = "Cash",
                Status = StatusAfterPayment,
                // The cashier checks the ID at the counter, so the discount is saved as already verified.
                DiscountRequest = discType ?? "None",
                DiscountType = discType,
                DiscountAmount = discount,
                DiscountIdNumber = discType != null ? dto.DiscountIdNumber!.Trim() : null,
                DiscountIdName = discType != null ? dto.DiscountIdName!.Trim() : null,
                DiscountVerifiedAt = discType != null ? PhTime.Now : null,
                DiscountVerifiedBy = discType != null ? (User.Identity?.Name ?? "cashier") : null,
                Total = total,
                EstimatedMinutes = dto.EstimatedMinutes,
                SpecialInstructions = note,
                CreatedAt = PhTime.Now,
                Items = dto.Items.Select(i => new OrderItem
                {
                    MenuItemId = i.MenuItemId,
                    Name = i.Name,
                    Quantity = i.Quantity,
                    Price = i.UnitPrice * i.Quantity,
                    Customization = i.Customization,
                    SpecialInstructions = i.SpecialInstructions
                }).ToList()
            };

            // Order + payment are saved together, so you never get one without the other.
            // CreateExecutionStrategy keeps this working even if retry-on-failure is enabled.
            try
            {
                var strategy = _db.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    await using var tx = await _db.Database.BeginTransactionAsync();

                    _db.Orders.Add(order);
                    await _db.SaveChangesAsync();

                    // Number comes from the identity Id, so two cashiers can never get the same number.
                    order.OrderNumber = $"ORD-{order.Id:D5}";

                    // Payment record, tied to the cashier's open shift (needed for Close Shift totals)
                    _db.Payments.Add(new Payment
                    {
                        OrderId = order.Id,
                        OrderNumber = order.OrderNumber,
                        TableNumber = "",
                        CustomerName = order.CustomerName ?? "",
                        Type = order.Type ?? "",
                        Amount = total,
                        PaidAt = PhTime.Now,
                        CashReceived = dto.AmountTendered,
                        ChangeGiven = dto.AmountTendered - total,
                        ShiftId = shift.Id
                    });

                    await _db.SaveChangesAsync();
                    await tx.CommitAsync();
                });
            }
            catch (Exception ex)
            {
                // Shows the real reason on the POS screen instead of a bare "HTTP 500".
                // You can remove the message detail once everything works.
                return StatusCode(500, new { error = "Server error: " + ex.GetBaseException().Message });
            }

            return Ok(new
            {
                orderNumber = order.OrderNumber,
                id = order.Id,
                subtotal,
                discountAmount = discount,
                discountType = discType,
                total,
                tendered = dto.AmountTendered,
                change = dto.AmountTendered - total,
                addOnTo = string.IsNullOrEmpty(note) ? null : dto.AddOnToOrderNumber!.Trim().ToUpper()
            });
        }
    }

    public class PosOrderDto
    {
        public string? CustomerName { get; set; }
        public string? AddOnToOrderNumber { get; set; }
        public decimal AmountTendered { get; set; }
        public string? DiscountType { get; set; }        // None | PWD | Senior
        public string? DiscountIdNumber { get; set; }
        public string? DiscountIdName { get; set; }
        public int EstimatedMinutes { get; set; }
        public List<PlaceOrderItemDto> Items { get; set; } = new();   // reuses your existing item DTO
    }
}
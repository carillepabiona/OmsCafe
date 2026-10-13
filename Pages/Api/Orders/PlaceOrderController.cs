using Microsoft.AspNetCore.Mvc;
using OmsCafe.Data;
using OmsCafe.Models;

namespace OmsCafe.Pages.Api
{
    [ApiController]
    [Route("api/orders")]
    [IgnoreAntiforgeryToken]
    public class PlaceOrderController : ControllerBase
    {
        private readonly AppDbContext _db;
        public PlaceOrderController(AppDbContext db) => _db = db;

        [HttpPost("place")]
        public async Task<IActionResult> PlaceOrder([FromBody] PlaceOrderDto dto)
        {
            if (dto == null || dto.Items == null || !dto.Items.Any())
                return BadRequest(new { error = "Invalid order" });

            // Only accept known values; anything else becomes "None" (Regular)
            var discountRequest = dto.DiscountRequest?.Trim() switch
            {
                "PWD" => "PWD",
                "Senior" => "Senior",
                _ => "None"
            };

            var lastId = _db.Orders.Any()
                ? _db.Orders.Max(o => o.Id)
                : 0;
            var orderNumber = $"ORD-{(lastId + 1):D5}";

            var order = new Order
            {
                OrderNumber = orderNumber,
                CustomerName = dto.CustomerName,
                Type = dto.OrderType,
                TableNumber = dto.TableNumber,
                PaymentMethod = dto.PaymentMethod,
                Status = "Waiting Payment",

                // What the customer asked for. The cashier verifies the ID later.
                DiscountRequest = discountRequest,

                // Always FULL price here. The cashier applies the discount after ID check.
                // No VAT/tax: total is the plain sum of the lines.
                Total = dto.Items.Sum(i => i.UnitPrice * i.Quantity),

                EstimatedMinutes = dto.EstimatedMinutes,
                CreatedAt = DateTime.UtcNow,
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

            _db.Orders.Add(order);
            await _db.SaveChangesAsync();

            return Ok(new { orderNumber = order.OrderNumber, id = order.Id });
        }
    }

    public class PlaceOrderDto
    {
        public string CustomerName { get; set; } = "";
        public string OrderType { get; set; } = "dine-in";
        public string? TableNumber { get; set; }
        public string PaymentMethod { get; set; } = "Cash";

        public string? DiscountRequest { get; set; }   // None | PWD | Senior
        public int EstimatedMinutes { get; set; }
        public List<PlaceOrderItemDto> Items { get; set; } = new();
    }

    public class PlaceOrderItemDto
    {
        public string MenuItemId { get; set; } = "";
        public string Name { get; set; } = "";
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public string? Customization { get; set; }
        public string? SpecialInstructions { get; set; }
    }
}

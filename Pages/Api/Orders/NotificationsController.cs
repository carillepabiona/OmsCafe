using Microsoft.AspNetCore.Mvc;
using OmsCafe.Data;
using OmsCafe.Models;
namespace OmsCafe.Pages.Api.Orders
{
    [ApiController]
    [Route("api/notifications")]
    [IgnoreAntiforgeryToken]
    public class NotificationsController : ControllerBase
    {
        private readonly AppDbContext _db;
        public NotificationsController(AppDbContext db) => _db = db;

        // Customer POSTs this when they tap "Call Staff"
        [HttpPost("call-staff")]
        public async Task<IActionResult> CallStaff([FromBody] CallStaffDto dto)
        {
            var notif = new StaffNotification
            {
                OrderNumber = dto.OrderNumber,
                TableNumber = dto.TableNumber,
                Message = dto.Message,
                IsAcknowledged = false,
                CreatedAt = DateTime.UtcNow
            };
            _db.StaffNotifications.Add(notif);
            await _db.SaveChangesAsync();
            return Ok(new { success = true });
        }

        // Cashier page polls this every 4s
        [HttpGet("call-staff/pending")]
        public IActionResult GetPending()
        {
            var pending = _db.StaffNotifications
                .Where(n => !n.IsAcknowledged)
                .OrderBy(n => n.CreatedAt)
                .Select(n => new {
                    id = n.Id,
                    orderNumber = n.OrderNumber,
                    tableNumber = n.TableNumber,
                    message = n.Message,
                    createdAt = n.CreatedAt
                })
                .ToList();
            return Ok(pending);
        }

        // Cashier taps "Acknowledge"
        [HttpPost("call-staff/{id}/acknowledge")]
        public async Task<IActionResult> Acknowledge(int id)
        {
            var notif = await _db.StaffNotifications.FindAsync(id);
            if (notif == null) return NotFound();
            notif.IsAcknowledged = true;
            await _db.SaveChangesAsync();
            return Ok(new { success = true });
        }
    }

    public class CallStaffDto
    {
        public string OrderNumber { get; set; } = "";
        public string? TableNumber { get; set; }
        public string? Message { get; set; }
    }
}

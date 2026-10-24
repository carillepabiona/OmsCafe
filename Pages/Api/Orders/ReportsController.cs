using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OmsCafe.Data;

namespace OmsCafe.Pages.Api.Models
{
    [ApiController]
    [Route("api/reports")]
    public class ReportsController : ControllerBase
    {
        private readonly AppDbContext _db;
        public ReportsController(AppDbContext db) => _db = db;

        [HttpGet]
        public async Task<IActionResult> Get(
            string period = "daily",
            string? date = null,
            string? month = null,
            int? year = null)
        {
            // ── Date range (safe parsing, no exceptions) ──
            var today = PhTime.Today;   // if CreatedAt is saved in UTC, use PhTime.Now.AddHours(8).Date
            DateTime from, to;

            if (period == "daily")
            {
                from = DateTime.TryParse(date, out var d) ? d.Date : today;
                to = from.AddDays(1);
            }
            else if (period == "monthly")
            {
                from = DateTime.TryParseExact((month ?? "") + "-01", "yyyy-MM-dd",
                           CultureInfo.InvariantCulture, DateTimeStyles.None, out var m)
                       ? m : new DateTime(today.Year, today.Month, 1);
                to = from.AddMonths(1);
            }
            else
            {
                var y = year ?? today.Year;
                from = new DateTime(y, 1, 1);
                to = from.AddYears(1);
            }

            // ── Only paid, non-cancelled orders (same source as the Payments page) ──
            var paidOrderIds = _db.Payments.Select(p => p.OrderId);

            var orders = await _db.Orders.AsNoTracking()
                .Include(o => o.Items)
                .Where(o => paidOrderIds.Contains(o.Id))
                .Where(o => o.CreatedAt >= from && o.CreatedAt < to)
                .Where(o => o.Status.ToLower() != "cancelled")
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();

            // ── Summary ──
            var totalOrders = orders.Count;
            var totalSales = orders.Sum(o => o.Total);
            var avgOrderValue = totalOrders > 0 ? totalSales / totalOrders : 0m;
            var totalItemsSold = orders.SelectMany(o => o.Items).Sum(i => i.Quantity);

            // ── Top items ──
            var topItems = orders
                .SelectMany(o => o.Items)
                .GroupBy(i => i.Name)
                .Select(g => new
                {
                    name = g.Key,
                    qtySold = g.Sum(i => i.Quantity),
                    revenue = g.Sum(i => i.Price * i.Quantity)
                })
                .OrderByDescending(x => x.qtySold)
                .Take(10)
                .ToList();

            // ── Payment breakdown ──
            var paymentBreakdown = orders
                .GroupBy(o => string.IsNullOrEmpty(o.PaymentMethod) ? "Cash" : o.PaymentMethod)
                .Select(g => new
                {
                    method = g.Key,
                    count = g.Count(),
                    amount = g.Sum(o => o.Total)
                })
                .OrderByDescending(x => x.amount)
                .ToList();

            // ── Order rows ──
            var orderRows = orders.Select(o => new
            {
                orderNumber = o.OrderNumber,
                customerName = o.CustomerName,
                orderType = o.Type,
                tableNumber = o.TableNumber,
                paymentMethod = o.PaymentMethod,
                itemCount = o.Items.Sum(i => i.Quantity),
                totalAmount = o.Total,
                status = o.Status,
                createdAt = o.CreatedAt
            }).ToList();

            return Ok(new
            {
                totalOrders,
                totalSales,
                avgOrderValue,
                totalItemsSold,
                topItems,
                paymentBreakdown,
                orders = orderRows
            });
        }
    }
}
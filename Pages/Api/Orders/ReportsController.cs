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
            // ── Build date range ───────────────────────────────────
            DateTime from, to;
            var today = DateTime.Today;

            if (period == "daily")
            {
                var d = date != null ? DateTime.Parse(date) : today;
                from = d;
                to = d.AddDays(1);
            }
            else if (period == "monthly")
            {
                var m = month != null ? DateTime.Parse(month + "-01") : new DateTime(today.Year, today.Month, 1);
                from = m;
                to = m.AddMonths(1);
            }
            else
            {
                var y = year ?? today.Year;
                from = new DateTime(y, 1, 1);
                to = new DateTime(y + 1, 1, 1);
            }

            // ── Query orders in range ──────────────────────────────
            var orders = await _db.Orders
            .Include(o => o.Items)          // was o.OrderItems
            .Where(o => o.CreatedAt >= from && o.CreatedAt < to
                     && o.Status != "Cancelled")
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

            // ── Summary ────────────────────────────────────────────
            var totalOrders = orders.Count;
            var totalSales = orders.Sum(o => o.Total);
            var avgOrderValue = totalOrders > 0 ? totalSales / totalOrders : 0;
            var totalItemsSold = orders.SelectMany(o => o.Items).Sum(i => i.Quantity);

            // ── Top items ──────────────────────────────────────────
            // Top items
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


            // ── Payment breakdown ──────────────────────────────────
            var paymentBreakdown = orders
                .GroupBy(o => o.PaymentMethod)
                .Select(g => new
                {
                    method = g.Key,
                    count = g.Count(),
                    amount = g.Sum(o => o.Total)
                })
                .OrderByDescending(x => x.amount)
                .ToList();

            // Order rows
            var orderRows = orders.Select(o => new
            {
                orderNumber = o.OrderNumber,
                customerName = o.CustomerName,
                orderType = o.Type,
                tableNumber = o.TableNumber,
                paymentMethod = o.PaymentMethod,
                itemCount = o.Items.Sum(i => i.Quantity),   // was o.OrderItems
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
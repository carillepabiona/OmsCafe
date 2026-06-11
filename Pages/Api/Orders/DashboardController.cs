using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OmsCafe.Data;

[ApiController]
[Route("api/dashboard")]
[IgnoreAntiforgeryToken]
public class DashboardController : ControllerBase
{
    private readonly AppDbContext _db;
    public DashboardController(AppDbContext db) => _db = db;

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary()
    {
        var today = DateTime.Today;
        var tomorrow = today.AddDays(1);

        var todayOrders = await _db.Orders
            .Where(o => o.CreatedAt >= today && o.CreatedAt < tomorrow)
            .ToListAsync();

        // lowercase to match DB values
        var cancelledStatuses = new[] { "cancelled" };
        var completedStatuses = new[] { "paid", "served", "completed" };
        var pendingStatuses = new[] { "pending", "awaiting_payment" };

        var validOrders = todayOrders.Where(o => !cancelledStatuses.Contains(o.Status)).ToList();
        var totalSales = validOrders.Sum(o => o.Total);
        var totalOrders = todayOrders.Count;
        var pendingOrders = todayOrders.Count(o => pendingStatuses.Contains(o.Status));
        var completedOrders = todayOrders.Count(o => completedStatuses.Contains(o.Status));
        var avgOrderValue = validOrders.Any() ? validOrders.Average(o => o.Total) : 0;

        var validOrderIds = validOrders.Select(o => o.Id).ToHashSet();
        var totalItemsSold = await _db.OrderItems
            .Where(oi => validOrderIds.Contains(oi.OrderId))
            .SumAsync(oi => (int?)oi.Quantity) ?? 0;

        var tables = await _db.Tables.ToListAsync();
        var totalTables = tables.Count;
        var occupiedTables = tables.Count(t => t.Status == "Occupied");
        var availableTables = tables.Count(t => t.Status == "Available");

        return Ok(new
        {
            totalSales,
            totalOrders,
            pendingOrders,
            completedOrders,
            avgOrderValue,
            totalItemsSold,
            totalTables,
            occupiedTables,
            availableTables
        });
    }

    [HttpGet("recent-orders")]
    public async Task<IActionResult> GetRecentOrders()
    {
        var today = DateTime.Today;
        var tomorrow = today.AddDays(1);

        var orders = await _db.Orders
            .Where(o => o.CreatedAt >= today && o.CreatedAt < tomorrow)
            .OrderByDescending(o => o.CreatedAt)
            .Take(10)
            .Select(o => new
            {
                orderNumber = o.OrderNumber,
                customerName = o.CustomerName,
                tableNumber = o.TableNumber,
                status = o.Status,
                totalAmount = o.Total,
                createdAt = o.CreatedAt
            })
            .ToListAsync();

        return Ok(orders);
    }

    [HttpGet("top-items")]
    public async Task<IActionResult> GetTopItems()
    {
        var today = DateTime.Today;
        var tomorrow = today.AddDays(1);

        // Pull valid order IDs first to avoid subquery translation issues
        var validOrderIds = await _db.Orders
            .Where(o => o.CreatedAt >= today && o.CreatedAt < tomorrow
                     && o.Status != "cancelled")
            .Select(o => o.Id)
            .ToListAsync();

        var topItems = await _db.OrderItems
            .Where(oi => validOrderIds.Contains(oi.OrderId))
            .GroupBy(oi => oi.Name)
            .Select(g => new
            {
                name = g.Key,
                qtySold = g.Sum(x => x.Quantity),
                revenue = g.Sum(x => x.Price * x.Quantity)
            })
            .OrderByDescending(x => x.qtySold)
            .Take(5)
            .ToListAsync();

        return Ok(topItems);
    }
}
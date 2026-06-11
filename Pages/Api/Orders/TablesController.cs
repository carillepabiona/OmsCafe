using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using OmsCafe.Data;
using OmsCafe.Models;
using OmsCafe.Hubs;

[ApiController]
[IgnoreAntiforgeryToken]
[Route("api/tables")]
public class TablesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IHubContext<TableHub> _hub;

    public TablesController(AppDbContext db, IHubContext<TableHub> hub)
    {
        _db = db;
        _hub = hub;
    }

    // GET /api/tables
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var tables = await _db.Tables
                              .OrderBy(t => t.TableNumber)
                              .Select(t => new {
                                  id = t.TableId,
                                  number = t.TableNumber,
                                  seats = t.Seats,
                                  status = t.Status
                              })
                              .ToListAsync();
        return Ok(tables);
    }

    // PATCH /api/tables/{id}/status
    [HttpPatch("{id}/status")]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateStatusDto dto)
    {
        if (!IsValidStatus(dto.Status)) return BadRequest("Invalid status.");

        var table = await _db.Tables.FindAsync(id);
        if (table == null) return NotFound();

        table.Status = dto.Status;
        await _db.SaveChangesAsync();

        // ── Broadcast to all POS dashboards ──
        await _hub.Clients.Group("tables-dashboard")
                  .SendAsync("TableStatusChanged", new
                  {
                      id = table.TableId,
                      number = table.TableNumber,
                      seats = table.Seats,
                      status = table.Status
                  });

        return Ok(new { table.TableId, table.TableNumber, table.Status });
    }

    // PATCH /api/tables/by-number/{number}/status
    [HttpPatch("by-number/{number}/status")]
    public async Task<IActionResult> UpdateStatusByNumber(int number, [FromBody] UpdateStatusDto dto)
    {
        if (!IsValidStatus(dto.Status)) return BadRequest("Invalid status.");

        var table = await _db.Tables.FirstOrDefaultAsync(t => t.TableNumber == number);
        if (table == null) return NotFound();

        table.Status = dto.Status;
        await _db.SaveChangesAsync();

        // ── Broadcast to all POS dashboards ──
        await _hub.Clients.Group("tables-dashboard")
                  .SendAsync("TableStatusChanged", new
                  {
                      id = table.TableId,
                      number = table.TableNumber,
                      seats = table.Seats,
                      status = table.Status
                  });

        return Ok(new { table.TableId, table.TableNumber, table.Status });
    }

    private static bool IsValidStatus(string status) =>
        new[] { "Available", "Occupied", "Reserved", "Cleaning" }.Contains(status);

    public class UpdateStatusDto
    {
        public string Status { get; set; } = "";
    }
}
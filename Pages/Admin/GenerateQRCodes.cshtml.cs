using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OmsCafe.Data;
using OmsCafe.Models;   // <-- adjust to the namespace of your CafeTable entity
using QRCoder;

namespace OmsCafe.Pages.Admin
{
    public class GenerateQRCodesModel : PageModel
    {
        private const string BaseUrl = "http://omscafeph.runasp.net"; // no trailing slash
        // private const string BaseUrl = "http://10.112.65.251:5000";

        private readonly AppDbContext _db;

        public GenerateQRCodesModel(AppDbContext db) => _db = db;

        public List<TableQRCode> QRCodes { get; set; } = new();
        public List<ArchivedTable> ArchivedTables { get; set; } = new();

        [TempData] public string? Message { get; set; }
        [TempData] public string? Error { get; set; }

        // ── READ ──────────────────────────────────────────────────
        public async Task OnGetAsync()
        {
            var tables = await _db.Tables.OrderBy(t => t.TableNumber).ToListAsync();
            bool anyUpdated = false;

            foreach (var table in tables)
            {
                if (table.IsArchived)
                {
                    ArchivedTables.Add(new ArchivedTable
                    {
                        TableNumber = table.TableNumber,
                        ArchivedAt = table.ArchivedAt
                    });
                    continue;
                }

                if (string.IsNullOrWhiteSpace(table.QRToken))
                {
                    table.QRToken = NewToken();
                    anyUpdated = true;
                }

                string url = $"{BaseUrl}/Menu/MenuDisplay?table={table.TableNumber}&token={table.QRToken}";
                QRCodes.Add(new TableQRCode
                {
                    TableNumber = table.TableNumber,
                    Url = url,
                    QRCodeBase64 = GenerateQRCode(url)
                });
            }

            if (anyUpdated) await _db.SaveChangesAsync();
        }

        // ── CREATE ────────────────────────────────────────────────
        public async Task<IActionResult> OnPostCreateAsync(int tableNumber)
        {
            if (tableNumber < 1)
            {
                Error = "Table number must be 1 or higher.";
            }
            else
            {
                var existing = await _db.Tables.FirstOrDefaultAsync(t => t.TableNumber == tableNumber);
                if (existing == null)
                {
                    _db.Tables.Add(new CafeTable { TableNumber = tableNumber, QRToken = NewToken() });
                    await _db.SaveChangesAsync();
                    Message = $"Table {tableNumber} added.";
                }
                else if (existing.IsArchived)
                {
                    Error = $"Table {tableNumber} is archived. Restore it from the Archived tables section.";
                }
                else
                {
                    Error = $"Table {tableNumber} already exists.";
                }
            }
            return RedirectToPage();
        }

        // ── ARCHIVE ───────────────────────────────────────────────
        public async Task<IActionResult> OnPostArchiveAsync(int tableNumber)
        {
            var table = await _db.Tables.FirstOrDefaultAsync(t => t.TableNumber == tableNumber && !t.IsArchived);
            if (table == null)
            {
                Error = "Table not found.";
                return RedirectToPage();
            }

            table.IsArchived = true;
            table.ArchivedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            Message = $"Table {tableNumber} archived. Its QR code is now inactive and order history is preserved.";
            return RedirectToPage();
        }

        // ── RESTORE ───────────────────────────────────────────────
        public async Task<IActionResult> OnPostRestoreAsync(int tableNumber)
        {
            var table = await _db.Tables.FirstOrDefaultAsync(t => t.TableNumber == tableNumber && t.IsArchived);
            if (table == null)
            {
                Error = "Archived table not found.";
                return RedirectToPage();
            }

            table.IsArchived = false;
            table.ArchivedAt = null;
            await _db.SaveChangesAsync();

            Message = $"Table {tableNumber} restored. Its QR code is active again.";
            return RedirectToPage();
        }

        // ── Helpers ───────────────────────────────────────────────
        private static string NewToken() => Guid.NewGuid().ToString("N")[..12].ToUpper();

        private static string GenerateQRCode(string url)
        {
            using QRCodeGenerator qrGenerator = new();
            using QRCodeData data = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
            return Convert.ToBase64String(new PngByteQRCode(data).GetGraphic(20));
        }

        public class TableQRCode
        {
            public int TableNumber { get; set; }
            public string Url { get; set; } = "";
            public string QRCodeBase64 { get; set; } = "";
        }

        public class ArchivedTable
        {
            public int TableNumber { get; set; }
            public DateTime? ArchivedAt { get; set; }
        }
    }
}
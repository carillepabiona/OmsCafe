using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OmsCafe.Data;
using OmsCafe.Models;   // <-- adjust to the namespace of your Table entity
using QRCoder;

namespace OmsCafe.Pages.Admin
{
    public class GenerateQRCodesModel : PageModel
    {
        /// <summary>
        /// The base URL for the application.
        /// </summary>
      //  private const string BaseUrl = "http://omscafe.runasp.net"; // no trailing slash

        private const string BaseUrl = "http://10.112.65.251:5000";
        private readonly AppDbContext _db;

        public GenerateQRCodesModel(AppDbContext db) => _db = db;

        public List<TableQRCode> QRCodes { get; set; } = new();

        [TempData] public string? Message { get; set; }
        [TempData] public string? Error { get; set; }

        // ── READ ──────────────────────────────────────────────────
        public void OnGet()
        {
            var tables = _db.Tables.OrderBy(t => t.TableNumber).ToList();
            bool anyUpdated = false;

            foreach (var table in tables)
            {
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

            if (anyUpdated) _db.SaveChanges();
        }

        // ── CREATE ────────────────────────────────────────────────
        public async Task<IActionResult> OnPostCreateAsync(int tableNumber)
        {
            if (tableNumber < 1)
                Error = "Table number must be 1 or higher.";
            else if (await _db.Tables.AnyAsync(t => t.TableNumber == tableNumber))
                Error = $"Table {tableNumber} already exists.";
            else
            {
                _db.Tables.Add(new CafeTable { TableNumber = tableNumber, QRToken = NewToken() });
                await _db.SaveChangesAsync();
                Message = $"Table {tableNumber} added.";
            }
            return RedirectToPage();
        }

        // ── UPDATE (change table number) ──────────────────────────
        public async Task<IActionResult> OnPostUpdateAsync(int currentNumber, int tableNumber)
        {
            var table = await _db.Tables.FirstOrDefaultAsync(t => t.TableNumber == currentNumber);
            if (table == null) Error = "Table not found.";
            else if (tableNumber < 1) Error = "Table number must be 1 or higher.";
            else if (await _db.Tables.AnyAsync(t => t.TableNumber == tableNumber && t.TableNumber != currentNumber))
                Error = $"Table {tableNumber} already exists.";
            else
            {
                int old = table.TableNumber;
                table.TableNumber = tableNumber;
                await _db.SaveChangesAsync();
                Message = $"Table {old} is now Table {tableNumber}. Reprint its QR code.";
            }
            return RedirectToPage();
        }


        // ── DELETE ────────────────────────────────────────────────
        public async Task<IActionResult> OnPostDeleteAsync(int tableNumber)
        {
            var table = await _db.Tables.FirstOrDefaultAsync(t => t.TableNumber == tableNumber);
            if (table == null) { Error = "Table not found."; return RedirectToPage(); }

            try
            {
                _db.Tables.Remove(table);
                await _db.SaveChangesAsync();
                Message = $"Table {table.TableNumber} deleted.";
            }
            catch (DbUpdateException)
            {
                // e.g. a foreign key from Orders → Tables
                Error = $"Table {table.TableNumber} can't be deleted because it is linked to existing orders.";
            }
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
    }
}

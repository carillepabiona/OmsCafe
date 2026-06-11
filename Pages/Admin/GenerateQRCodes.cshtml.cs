using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QRCoder;
using OmsCafe.Data;

namespace OmsCafe.Pages.Admin
{
    public class GenerateQRCodesModel : PageModel
    {
        private readonly AppDbContext _db;

        public GenerateQRCodesModel(AppDbContext db)
        {
            _db = db;
        }

        public List<TableQRCode> QRCodes { get; set; } = new();

        public void OnGet()
        {
            string baseUrl = "http://192.168.254.101:5114";

            var tables = _db.Tables
                            .OrderBy(t => t.TableNumber)
                            .ToList();

            bool anyUpdated = false;

            foreach (var table in tables)
            {
                // ── FIX: ensure every table has a token ──
                if (string.IsNullOrWhiteSpace(table.QRToken))
                {
                    table.QRToken = Guid.NewGuid().ToString("N")[..12].ToUpper();
                    anyUpdated = true;
                }

                string url = $"{baseUrl}/Menu/MenuDisplay?table={table.TableNumber}&token={table.QRToken}";

                QRCodes.Add(new TableQRCode
                {
                    TableNumber = table.TableNumber,
                    Url = url,
                    QRCodeBase64 = GenerateQRCode(url)
                });
            }

            // Save any newly generated tokens back to the DB
            if (anyUpdated)
                _db.SaveChanges();
        }

        private string GenerateQRCode(string url)
        {
            using QRCodeGenerator qrGenerator = new();

            using QRCodeData qrCodeData =
                qrGenerator.CreateQrCode(url,
                    QRCodeGenerator.ECCLevel.Q);

            PngByteQRCode qrCode = new(qrCodeData);

            byte[] qrBytes = qrCode.GetGraphic(20);

            return Convert.ToBase64String(qrBytes);
        }

        public class TableQRCode
        {
            public int TableNumber { get; set; }
            public string Url { get; set; } = "";
            public string QRCodeBase64 { get; set; } = "";
        }
    }
}
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OmsCafe.Data;
using OmsCafe.Models;

namespace OmsCafe.Pages.Admin
{
    // No [Authorize]: this app logs in with Session, not an ASP.NET auth scheme.
    // Access is checked in OnPageHandlerExecuting below.
    public class CashReconciliationModel : PageModel
    {
        private readonly AppDbContext _db;
        public CashReconciliationModel(AppDbContext db) => _db = db;

        // ── Session settings: these now match LoginModel.OnPostLoginAsync ──
        private const string SessionUserId = "UserId";
        private const string SessionUserName = "FullName";   // login stores "FullName" and "Username"
        private const string SessionRole = "Role";
        private const string LoginPage = "/Login";
        private static readonly string[] AllowedRoles = { "Admin", "Manager" };

        // Runs before every handler (OnGet and OnPostReview)
        public override void OnPageHandlerExecuting(PageHandlerExecutingContext context)
        {
            var userId = HttpContext.Session.GetString(SessionUserId);
            var role = HttpContext.Session.GetString(SessionRole)?.Trim();

            if (string.IsNullOrEmpty(userId))
            {
                context.Result = RedirectToPage(LoginPage);
                return;
            }

            if (string.IsNullOrEmpty(role) ||
                !AllowedRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
            {
                // DEBUG: shows what the session actually holds.
                // Once it works, replace this with:
                //   context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
                context.Result = new ContentResult
                {
                    StatusCode = StatusCodes.Status403Forbidden,
                    Content = $"Forbidden. Session role = '{role ?? "null"}'. Allowed: {string.Join(", ", AllowedRoles)}"
                };
                return;
            }

            base.OnPageHandlerExecuting(context);
        }

        // ── Filters ──────────────────────────────────────────────────
        [BindProperty(SupportsGet = true)] public Guid? CashierId { get; set; }
        [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
        [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }
        [BindProperty(SupportsGet = true)] public string? Result { get; set; }
        [BindProperty(SupportsGet = true)] public string? Review { get; set; }
        [BindProperty(SupportsGet = true, Name = "report")] public int? ReportId { get; set; }

        // ── View models ──────────────────────────────────────────────
        public class Row
        {
            public int Id { get; set; }
            public string CashierName { get; set; } = "";
            public DateTime OpenedAt { get; set; }
            public DateTime SubmittedAt { get; set; }
            public decimal Expected { get; set; }
            public decimal Actual { get; set; }
            public decimal Variance { get; set; }
            public string Result { get; set; } = "";
            public string ReviewStatus { get; set; } = "Pending";
        }

        public class DetailView
        {
            public Row Row { get; set; } = new();
            public int ShiftId { get; set; }
            public DateTime? ClosedAt { get; set; }
            public decimal OpeningFloat { get; set; }
            public int PaymentCount { get; set; }
            public decimal RecordedSales { get; set; }     // sum of Payment.Amount
            public decimal CashCollected { get; set; }     // received minus change given
            public decimal LedgerExpected => OpeningFloat + CashCollected;
            public string? Explanation { get; set; }
            public string? ReviewedByName { get; set; }
            public DateTime? ReviewedAt { get; set; }
            public string? ManagerNotes { get; set; }
        }

        public List<Row> Rows { get; private set; } = new();
        public List<(Guid Id, string Name)> Cashiers { get; private set; } = new();
        public DetailView? Detail { get; private set; }

        public int PendingCount => Rows.Count(r => r.ReviewStatus == "Pending");
        public int FlaggedCount => Rows.Count(r => r.ReviewStatus == "Flagged");
        public decimal TotalShort => Rows.Where(r => r.Variance < 0).Sum(r => -r.Variance);
        public decimal TotalOver => Rows.Where(r => r.Variance > 0).Sum(r => r.Variance);

        // ── GET ──────────────────────────────────────────────────────
        public void OnGet()
        {
            Cashiers = _db.Shifts
                .GroupBy(s => s.CashierId)
                .Select(g => new { Id = g.Key, Name = g.Max(x => x.CashierName) })
                .AsEnumerable()
                .Select(x => (x.Id, x.Name))
                .OrderBy(x => x.Name)
                .ToList();

            var q = from r in _db.ShiftClosingReports
                    join s in _db.Shifts on r.ShiftId equals s.Id
                    select new { r, s };

            if (CashierId.HasValue) q = q.Where(x => x.s.CashierId == CashierId.Value);
            if (From.HasValue) q = q.Where(x => x.r.SubmittedAt >= From.Value.Date);
            if (To.HasValue) q = q.Where(x => x.r.SubmittedAt < To.Value.Date.AddDays(1));
            if (!string.IsNullOrEmpty(Result)) q = q.Where(x => x.r.Result == Result);
            if (!string.IsNullOrEmpty(Review)) q = q.Where(x => x.r.ReviewStatus == Review);

            Rows = q.OrderByDescending(x => x.r.SubmittedAt)
                .Select(x => new Row
                {
                    Id = x.r.Id,
                    CashierName = x.s.CashierName,
                    OpenedAt = x.s.OpenedAt,
                    SubmittedAt = x.r.SubmittedAt,
                    Expected = x.r.ExpectedCash,
                    Actual = x.r.ActualCash,
                    Variance = x.r.Variance,
                    Result = x.r.Result,
                    ReviewStatus = x.r.ReviewStatus
                })
                .ToList();

            if (ReportId.HasValue) LoadDetail(ReportId.Value);
        }

        private void LoadDetail(int reportId)
        {
            var d = (from r in _db.ShiftClosingReports
                     join s in _db.Shifts on r.ShiftId equals s.Id
                     where r.Id == reportId
                     select new { r, s }).FirstOrDefault();
            if (d == null) return;

            var pays = _db.Payments.Where(p => p.ShiftId == d.s.Id).ToList();

            Detail = new DetailView
            {
                Row = new Row
                {
                    Id = d.r.Id,
                    CashierName = d.s.CashierName,
                    OpenedAt = d.s.OpenedAt,
                    SubmittedAt = d.r.SubmittedAt,
                    Expected = d.r.ExpectedCash,
                    Actual = d.r.ActualCash,
                    Variance = d.r.Variance,
                    Result = d.r.Result,
                    ReviewStatus = d.r.ReviewStatus
                },
                ShiftId = d.s.Id,
                ClosedAt = d.s.ClosedAt,
                OpeningFloat = d.s.OpeningFloat,
                PaymentCount = pays.Count,
                RecordedSales = pays.Sum(p => p.Amount),
                CashCollected = pays.Sum(p => (p.CashReceived ?? 0m) - (p.ChangeGiven ?? 0m)),
                Explanation = d.r.Explanation,
                ReviewedByName = d.r.ReviewedByName,
                ReviewedAt = d.r.ReviewedAt,
                ManagerNotes = d.r.ManagerNotes
            };
        }

        // ── POST: verify or flag ─────────────────────────────────────
        public IActionResult OnPostReview(int reportId, string decision, string? notes, string? returnQuery)
        {
            var report = _db.ShiftClosingReports.Find(reportId);
            if (report == null) return NotFound();

            var back = Url.Page("CashReconciliation")
                + (returnQuery != null && returnQuery.StartsWith("?") ? returnQuery : $"?report={reportId}");

            if (report.ReviewStatus != "Pending")
            {
                TempData["Error"] = "This report has already been reviewed.";
                return Redirect(back);
            }

            if (decision != "Verified" && decision != "Flagged") return BadRequest();

            if (decision == "Flagged" && string.IsNullOrWhiteSpace(notes))
            {
                TempData["Error"] = "Add a note explaining what needs investigating before flagging.";
                return Redirect(back);
            }

            report.ReviewStatus = decision;
            report.ReviewedBy = Guid.TryParse(HttpContext.Session.GetString(SessionUserId), out var uid) ? uid : null;
            report.ReviewedByName = HttpContext.Session.GetString(SessionUserName) ?? "Manager";
            report.ReviewedAt = PhTime.Now;
            report.ManagerNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
            _db.SaveChanges();

            TempData["Ok"] = decision == "Verified" ? "Report verified." : "Report flagged for investigation.";
            return Redirect(back);
        }
    }
}
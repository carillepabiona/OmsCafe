using System.ComponentModel.DataAnnotations;

namespace OmsCafe.Models
{
    public static class ShiftStatus
    {
        public const string Open = "Open";
        public const string PendingReview = "PendingReview";
        public const string Approved = "Approved";
        public const string Rejected = "Rejected";
    }

    public class Shift
    {
        public int Id { get; set; }
        public Guid CashierId { get; set; }
        public string CashierName { get; set; } = "";
        public DateTime OpenedAt { get; set; }
        public DateTime? ClosedAt { get; set; }
        public decimal OpeningFloat { get; set; }
        public string Status { get; set; } = ShiftStatus.Open;

        [Timestamp] public byte[]? RowVersion { get; set; }   // blocks double-submit

        public ShiftClosingReport? Report { get; set; }
    }

    public class ShiftClosingReport
    {
        public int Id { get; set; }
        public int ShiftId { get; set; }

        public decimal ExpectedCash { get; set; }   // snapshot at submit time
        public decimal ActualCash { get; set; }
        public decimal Variance { get; set; }       // Actual - Expected
        public string Result { get; set; } = "";    // Balanced | Short | Over

        public string ReviewStatus { get; set; } = "Pending";
        public string? Explanation { get; set; }
        public DateTime SubmittedAt { get; set; }

        // Filled in by the manager review step (next feature)
        public Guid? ReviewedBy { get; set; }
        public string? ReviewedByName { get; set; }
        public DateTime? ReviewedAt { get; set; }

        public string? ManagerNotes { get; set; }

        public Shift Shift { get; set; } = null!;
    }
}

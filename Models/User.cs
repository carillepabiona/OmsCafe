using System.ComponentModel.DataAnnotations;

namespace OmsCafe.Models
{
    // Approval state of an account.
    // Self-registered accounts start as Pending; accounts created by an admin are Approved.
    public static class AccountApproval
    {
        public const string Pending = "Pending";
        public const string Approved = "Approved";
        public const string Rejected = "Rejected";
    }

    public class User
    {
        public Guid UserId { get; set; }
        public string FullName { get; set; } = string.Empty;

        [MaxLength(20)]
        public string? ContactNumber { get; set; }

        [MaxLength(250)]
        public string? Address { get; set; }
        public string Username { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public DateTime? CreatedAt { get; set; }
        public Guid RoleId { get; set; }

        // ── NEW: approval ───────────────────────────────────────
        public string ApprovalStatus { get; set; } = AccountApproval.Approved;
        public DateTime? ReviewedAt { get; set; }
        public string? ReviewedBy { get; set; }      // username of the admin who approved / rejected

        // ── NEW: archive (replaces delete) ──────────────────────
        public bool IsArchived { get; set; }
        public DateTime? ArchivedAt { get; set; }

        public Role? Role { get; set; }
    }
}
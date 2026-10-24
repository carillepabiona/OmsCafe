using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OmsCafe.Models;

public enum PasswordResetStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}

public class PasswordResetRequest
{
    [Key]
    public Guid RequestId { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    // BCrypt hash of the password the user asked for (never the plain text)
    public string NewPasswordHash { get; set; } = "";

    public DateTime RequestedAt { get; set; }
    public PasswordResetStatus Status { get; set; } = PasswordResetStatus.Pending;

    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedBy { get; set; }
}
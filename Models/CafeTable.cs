using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OmsCafe.Models
{
    [Table("Tables")]
    public class CafeTable
    {
        [Key]
        public Guid TableId { get; set; } = Guid.NewGuid();

        [Required]
        public int TableNumber { get; set; }

        public int Seats { get; set; } = 2;

        [Required]
        [MaxLength(20)]
        public string Status { get; set; } = "Available";

        public DateTime? CreatedAt { get; set; } = PhTime.Now;

        // In your Table entity / DB
        public string? QRToken { get; set; }   // ← must exist

        public bool IsArchived { get; set; } = false;
        public DateTime? ArchivedAt { get; set; }
    }
}
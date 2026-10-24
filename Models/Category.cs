using System.ComponentModel.DataAnnotations;

namespace OmsCafe.Models
{
    public class Category
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required, MaxLength(100)]
        public string CategoryName { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Description { get; set; }

        [MaxLength(20)]
        public string Color { get; set; } = "#8B4513";

        public int DisplayOrder { get; set; }
        public bool IsActive { get; set; } = true;
        public bool IsDeleted { get; set; } = false;   // ← ADD THIS
        public DateTime CreatedAt { get; set; } = PhTime.Now;
        public DateTime UpdatedAt { get; set; } = PhTime.Now;

        public ICollection<MenuItem> MenuItems { get; set; } = new List<MenuItem>();
    }
}

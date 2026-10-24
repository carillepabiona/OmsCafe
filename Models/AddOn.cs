using System.ComponentModel.DataAnnotations;

namespace OmsCafe.Models
{
    public class Addon
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? Category { get; set; }

        public decimal Price { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = PhTime.Now;
        public DateTime UpdatedAt { get; set; } = PhTime.Now;

        public ICollection<MenuItem> MenuItems { get; set; } = new List<MenuItem>();
        public ICollection<MenuItemAddon> MenuItemAddons { get; set; } = new List<MenuItemAddon>();

    }
}

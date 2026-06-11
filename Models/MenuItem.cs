using System.ComponentModel.DataAnnotations;

namespace OmsCafe.Models
{
    public class MenuItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid CategoryId { get; set; }

        [Required, MaxLength(150)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string? Description { get; set; }

        public decimal Price { get; set; }
        public decimal Cost { get; set; }
        public int PreparationTime { get; set; } = 5;
        public bool IsAvailable { get; set; } = true;
        public TimeOnly AvailableFrom { get; set; } = new TimeOnly(6, 0);
        public TimeOnly AvailableUntil { get; set; } = new TimeOnly(22, 0);
        public string? ImageBase64 { get; set; }
        public string? ImageMimeType { get; set; }
        public int DisplayOrder { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public Category Category { get; set; } = null!;
        public ICollection<Customization> Customizations { get; set; } = new List<Customization>();
        public ICollection<Addon> Addons { get; set; } = new List<Addon>();

        public ICollection<MenuItemCustomization> MenuItemCustomizations { get; set; } = new List<MenuItemCustomization>();
        public ICollection<MenuItemAddon> MenuItemAddons { get; set; } = new List<MenuItemAddon>();
    }
}

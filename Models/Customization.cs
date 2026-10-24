using System.ComponentModel.DataAnnotations;

namespace OmsCafe.Models
{
    public class Customization
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [Required, MaxLength(20)]
        public string Type { get; set; } = "Radio"; // "Radio" or "Checkbox"

        public bool IsRequired { get; set; }
        public int DisplayOrder { get; set; }
        public DateTime CreatedAt { get; set; } = PhTime.Now;
        public DateTime UpdatedAt { get; set; } = PhTime.Now;

        public ICollection<CustomizationOption> Options { get; set; } = new List<CustomizationOption>();
        public ICollection<MenuItem> MenuItems { get; set; } = new List<MenuItem>();
        public ICollection<MenuItemCustomization> MenuItemCustomizations { get; set; } = new List<MenuItemCustomization>();

    }
}

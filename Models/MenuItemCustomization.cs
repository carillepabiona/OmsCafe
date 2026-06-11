namespace OmsCafe.Models
{
    public class MenuItemCustomization
    {
        public Guid MenuItemId { get; set; }
        public MenuItem MenuItem { get; set; } = null!;

        public Guid CustomizationId { get; set; }
        public Customization Customization { get; set; } = null!;
    }
}

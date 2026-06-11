namespace OmsCafe.Models
{
    public class MenuItemAddon
    {
        public Guid MenuItemId { get; set; }
        public MenuItem MenuItem { get; set; } = null!;

        public Guid AddonId { get; set; }
        public Addon Addon { get; set; } = null!;
    }
}

namespace OmsCafe.Models
{
    public class OrderItem
    {
        public int Id { get; set; }
        public int OrderId { get; set; }
        public string MenuItemId { get; set; } = "";
        public string Name { get; set; } = "";
        public int Quantity { get; set; }
        public decimal Price { get; set; }
        public string? Customization { get; set; }
        public string? SpecialInstructions { get; set; }
        public Order Order { get; set; } = null!;
    }
}

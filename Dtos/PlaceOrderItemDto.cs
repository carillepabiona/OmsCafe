namespace OmsCafe.Dtos
{
    public class PlaceOrderItemDto
    {
        public string MenuItemId { get; set; } = "";
        public string Name { get; set; } = "";
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public string? Customization { get; set; }
        public string? SpecialInstructions { get; set; }
    }
}

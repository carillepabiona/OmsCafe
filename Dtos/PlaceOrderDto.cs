namespace OmsCafe.Dtos
{
    public class PlaceOrderDto
    {
        public string CustomerName { get; set; } = "";
        public string OrderType { get; set; } = "dine-in";
        public string? TableNumber { get; set; }
        public string PaymentMethod { get; set; } = "Cash";
        public int EstimatedMinutes { get; set; }
        public List<PlaceOrderItemDto> Items { get; set; } = new();
    }
}

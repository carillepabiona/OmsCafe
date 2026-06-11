using static OmsCafe.Pages.Cashier.OrdersModel;

namespace OmsCafe.Models
{
    public class Order
    {
        public int Id { get; set; }
        public string OrderNumber { get; set; } = "";
        public string CustomerName { get; set; } = "";
        public string Type { get; set; } = "dine-in";   // dine-in | take-out
        public string? TableNumber { get; set; }
        public string PaymentMethod { get; set; } = "Cash";
        public string Status { get; set; } = "pending";
        public decimal Total { get; set; }
        public int EstimatedMinutes { get; set; }
        public string SpecialInstructions { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public DateTime? ServedAt { get; set; }

        public List<OrderItem> Items { get; set; } = new();
    }
}

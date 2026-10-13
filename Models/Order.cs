namespace OmsCafe.Models
{
    public class Order
    {
        public int Id { get; set; }
        public string OrderNumber { get; set; } = "";
        public string CustomerName { get; set; } = "";
        public string Type { get; set; } = "dine-in";
        public string? TableNumber { get; set; }
        public string PaymentMethod { get; set; } = "Cash";
        public string Status { get; set; } = "pending";
        public decimal Total { get; set; }              // FINAL amount (discounted once applied)
        public int EstimatedMinutes { get; set; }
        public string SpecialInstructions { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public DateTime? ServedAt { get; set; }

        // ── Discount ──
        public string? DiscountRequest { get; set; } = "None";   // what the customer asked for: None | PWD | Senior
        public string? DiscountType { get; set; }                // set only after cashier verifies: PWD | Senior
        public decimal? DiscountAmount { get; set; } = 0m;             // total peso reduction (VAT exemption + 20%)
        public string? DiscountIdNumber { get; set; }
        public string? DiscountIdName { get; set; }
        public DateTime? DiscountVerifiedAt { get; set; }
        public string? DiscountVerifiedBy { get; set; }

        public List<OrderItem> Items { get; set; } = new();
    }
}
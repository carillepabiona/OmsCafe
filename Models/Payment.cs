namespace OmsCafe.Models
{
    public class Payment
    {
        public int Id { get; set; }
        public int OrderId { get; set; }
        public string OrderNumber { get; set; } = "";
        public string TableNumber { get; set; } = "";
        public string CustomerName { get; set; } = "";
        public string Type { get; set; } = "";
        public decimal Amount { get; set; }
        public DateTime PaidAt { get; set; }

        public Order Order { get; set; } = null!;
    }
}

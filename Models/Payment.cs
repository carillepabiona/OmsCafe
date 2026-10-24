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

        public decimal? CashReceived { get; set; }
        public decimal? ChangeGiven { get; set; }

        public int? ShiftId { get; set; }   // null for payments made before shifts existed

        public Order Order { get; set; } = null!;
    }
}

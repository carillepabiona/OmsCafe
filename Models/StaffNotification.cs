namespace OmsCafe.Models
{
    public class StaffNotification
    {
        public int Id { get; set; }
        public string OrderNumber { get; set; } = "";
        public string? TableNumber { get; set; }
        public string? Message { get; set; }
        public bool IsAcknowledged { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}

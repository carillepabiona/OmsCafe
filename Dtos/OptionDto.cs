namespace OmsCafe.Dtos
{
    public class OptionDto
    {
        public Guid Id { get; set; }
        public string OptionLabel { get; set; } = "";
        public decimal PriceModifier { get; set; }
        public bool IsDefault { get; set; }
    }
}

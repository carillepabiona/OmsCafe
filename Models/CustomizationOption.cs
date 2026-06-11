using System.ComponentModel.DataAnnotations;

namespace OmsCafe.Models
{
    public class CustomizationOption
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid CustomizationId { get; set; }

        [Required, MaxLength(100)]
        public string OptionLabel { get; set; } = string.Empty;

        public decimal PriceModifier { get; set; }
        public bool IsDefault { get; set; }
        public int DisplayOrder { get; set; }

        public Customization Customization { get; set; } = null!;
    }
}

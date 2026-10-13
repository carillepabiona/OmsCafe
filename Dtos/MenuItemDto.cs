namespace OmsCafe.Dtos
{
    public class MenuItemDto
    {
        public Guid Id { get; set; }
        public Guid CategoryId { get; set; }
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public decimal Price { get; set; }
        public int PreparationTime { get; set; }
        public bool IsAvailable { get; set; }
        public bool HasImage { get; set; }
        public string? ImageUrl { get; set; }
        public int DisplayOrder { get; set; }
        public List<CustomizationDto> Customizations { get; set; } = new();
        public List<AddonDto> Addons { get; set; } = new();
    }
}

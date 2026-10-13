namespace OmsCafe.Dtos
{
    public class CustomizationDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public bool IsRequired { get; set; }
        public int DisplayOrder { get; set; }
        public List<OptionDto> Options { get; set; } = new();
    }
}

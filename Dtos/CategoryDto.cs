namespace OmsCafe.Dtos
{
    public class CategoryDto
    {
        public Guid Id { get; set; }
        public string CategoryName { get; set; } = "";
        public string? Color { get; set; }
        public int DisplayOrder { get; set; }
    }
}

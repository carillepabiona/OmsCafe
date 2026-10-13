using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OmsCafe.Data;

namespace OmsCafe.Controller
{
    [ApiController]
    [Route("api/menu-items")]
    public class MenuItemImageController : ControllerBase
    {
        private readonly AppDbContext _db;

        public MenuItemImageController(AppDbContext db)
        {
            _db = db;
        }

        [HttpGet("{id:guid}/image")]
        [ResponseCache(Duration = 3600)]
        public async Task<IActionResult> GetImage(Guid id)   // ← was int, must be Guid
        {
            var item = await _db.MenuItems
                .Where(m => m.Id == id)
                .Select(m => new { m.ImageBase64, m.ImageMimeType })
                .FirstOrDefaultAsync();

            if (item == null || string.IsNullOrEmpty(item.ImageBase64))
                return NotFound();

            var bytes = Convert.FromBase64String(item.ImageBase64);
            var mime = string.IsNullOrEmpty(item.ImageMimeType) ? "image/jpeg" : item.ImageMimeType;

            return File(bytes, mime);
        }
    }
}
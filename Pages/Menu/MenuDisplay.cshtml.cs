using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using OmsCafe.Data;

namespace OmsCafe.Pages.Menu
{
    // ── View DTOs ─────────────────────────────────────────────────────────
    // Serialized camelCase by the .cshtml, so property names match what the JS reads.
    // None of them carry image data: that is served by OnGetImageAsync.
    public class CategoryDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string? Color { get; set; }
        public int DisplayOrder { get; set; }
    }

    public class OptionDto
    {
        public Guid Id { get; set; }
        public string Label { get; set; } = "";
        public decimal PriceModifier { get; set; }
        public bool IsDefault { get; set; }
    }

    public class CustomizationDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";          // "Radio" / "Checkbox"
        public bool IsRequired { get; set; }
        public int DisplayOrder { get; set; }
        public List<OptionDto> Options { get; set; } = new();
    }

    public class AddonDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public decimal Price { get; set; }
        public bool IsActive { get; set; }
    }

    public class MenuItemDto
    {
        public Guid Id { get; set; }
        public Guid CategoryId { get; set; }
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public decimal Price { get; set; }
        public int? PreparationTime { get; set; }
        public bool IsAvailable { get; set; }
        public bool HasImage { get; set; }
        public int DisplayOrder { get; set; }
        public List<CustomizationDto> Customizations { get; set; } = new();
        public List<AddonDto> Addons { get; set; } = new();
    }

    public class MenuDisplayModel : PageModel
    {
        private const string MenuCacheKey = "menu-display";

        private readonly AppDbContext _db;
        private readonly IMemoryCache _cache;

        public MenuDisplayModel(AppDbContext db, IMemoryCache cache)
        {
            _db = db;
            _cache = cache;
        }

        [BindProperty(SupportsGet = true)]
        public required string Table { get; set; }

        [BindProperty(SupportsGet = true)]
        public string Token { get; set; } = "";

        public List<CategoryDto> Categories { get; set; } = new();
        public List<MenuItemDto> MenuItems { get; set; } = new();

        public async Task OnGetAsync(string table, string token)
        {
            Table = table;
            Token = token;

            // If no token in URL and no session cookie fallback,
            // we let the JS handle it client-side.
            if (!string.IsNullOrEmpty(Token))
            {
                TempData["qr_token"] = Token;
            }

            // The menu rarely changes, so don't hit SQL Server on every QR scan.
            // Edits show up within 2 minutes, or call InvalidateMenuCache() from your admin save code.
            var data = await _cache.GetOrCreateAsync(MenuCacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2);
                return await LoadMenuAsync();
            });

            Categories = data!.Categories;
            MenuItems = data.Items;
        }

        // GET /Menu/MenuDisplay?handler=Image&id=<guid>
        // Serves one item's image as a real file so the browser can cache and lazy-load it.
        public async Task<IActionResult> OnGetImageAsync(Guid id)
        {
            var img = await _db.MenuItems
                .AsNoTracking()
                .Where(m => m.Id == id
                            && !m.Category.IsDeleted && m.Category.IsActive
                            && m.ImageBase64 != null && m.ImageBase64 != "")
                .Select(m => new { m.ImageBase64, m.ImageMimeType })
                .FirstOrDefaultAsync();

            if (img == null) return NotFound();

            byte[] bytes;
            try { bytes = Convert.FromBase64String(img.ImageBase64!); }
            catch (FormatException) { return NotFound(); }

            Response.Headers.CacheControl = "public,max-age=3600";
            return File(bytes, string.IsNullOrWhiteSpace(img.ImageMimeType) ? "image/jpeg" : img.ImageMimeType);
        }

        // Call from admin create/update/delete code so edits show immediately.
        public static void InvalidateMenuCache(IMemoryCache cache) => cache.Remove(MenuCacheKey);

        // ── Data loading ──────────────────────────────────────────────────
        private async Task<MenuData> LoadMenuAsync()
        {
            var categories = await _db.Categories
                .AsNoTracking()
                .Where(c => c.IsActive && !c.IsDeleted)
                .OrderBy(c => c.DisplayOrder)
                .Select(c => new CategoryDto
                {
                    Id = c.Id,
                    Name = c.CategoryName,
                    Color = c.Color,
                    DisplayOrder = c.DisplayOrder
                })
                .ToListAsync();

            // Project to a slim shape: the ImageBase64 blob column is never selected here.
            // The enum/string Type is converted in memory to avoid any SQL translation issues.
            var rows = await _db.MenuItems
                .AsNoTracking()
                .Where(m => !m.Category.IsDeleted && m.Category.IsActive)
                .OrderBy(m => m.Category.DisplayOrder)
                    .ThenBy(m => m.DisplayOrder)
                .Select(m => new
                {
                    m.Id,
                    m.CategoryId,
                    m.Name,
                    m.Description,
                    m.Price,
                    m.PreparationTime,
                    m.IsAvailable,
                    HasImage = m.ImageBase64 != null && m.ImageBase64 != "",
                    m.DisplayOrder,
                    Customizations = m.MenuItemCustomizations.Select(mic => new
                    {
                        mic.Customization.Id,
                        mic.Customization.Name,
                        mic.Customization.Type,
                        mic.Customization.IsRequired,
                        mic.Customization.DisplayOrder,
                        Options = mic.Customization.Options
                            .OrderBy(o => o.DisplayOrder)
                            .Select(o => new
                            {
                                o.Id,
                                o.OptionLabel,
                                o.PriceModifier,
                                o.IsDefault
                            }).ToList()
                    }).ToList(),
                    Addons = m.MenuItemAddons.Select(mia => new
                    {
                        mia.Addon.Id,
                        mia.Addon.Name,
                        mia.Addon.Price,
                        mia.Addon.IsActive
                    }).ToList()
                })
                .AsSplitQuery()
                .ToListAsync();

            var items = rows.Select(m => new MenuItemDto
            {
                Id = m.Id,
                CategoryId = m.CategoryId,
                Name = m.Name,
                Description = m.Description,
                Price = m.Price,
                PreparationTime = m.PreparationTime,
                IsAvailable = m.IsAvailable,
                HasImage = m.HasImage,
                DisplayOrder = m.DisplayOrder,
                Customizations = m.Customizations.Select(c => new CustomizationDto
                {
                    Id = c.Id,
                    Name = c.Name,
                    Type = c.Type?.ToString() ?? "",
                    IsRequired = c.IsRequired,
                    DisplayOrder = c.DisplayOrder,
                    Options = c.Options.Select(o => new OptionDto
                    {
                        Id = o.Id,
                        Label = o.OptionLabel,
                        PriceModifier = o.PriceModifier,
                        IsDefault = o.IsDefault
                    }).ToList()
                }).ToList(),
                Addons = m.Addons.Select(a => new AddonDto
                {
                    Id = a.Id,
                    Name = a.Name,
                    Price = a.Price,
                    IsActive = a.IsActive
                }).ToList()
            }).ToList();

            return new MenuData(categories, items);
        }

        private sealed record MenuData(List<CategoryDto> Categories, List<MenuItemDto> Items);
    }
}
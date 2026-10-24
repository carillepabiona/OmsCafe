using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using OmsCafe.Data;
using System.Text.Json;

namespace OmsCafe.Pages.TakeOut
{
    // ── DTOs (serialized camelCase; names match what the JS reads) ────────
    // No image data in any of them: images are served by OnGetImageAsync.
    public class TakeOutCategoryDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string Sub { get; set; } = "";
    }

    public class TakeOutOptionDto
    {
        public Guid Id { get; set; }
        public string Label { get; set; } = "";
        public decimal PriceModifier { get; set; }
        public bool IsDefault { get; set; }
    }

    public class TakeOutCustomizationDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";     // "Radio" / "Checkbox"
        public bool Required { get; set; }
        public List<TakeOutOptionDto> Options { get; set; } = new();
    }

    public class TakeOutAddonDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string Category { get; set; } = "";
        public decimal Price { get; set; }
    }

    // A record so each request can copy it with `with { IsAvailable = ... }`
    // without mutating the instance that lives in the cache.
    public record TakeOutItemDto
    {
        public Guid Id { get; init; }
        public Guid Cat { get; init; }
        public string Name { get; init; } = "";
        public string Desc { get; init; } = "";
        public decimal Price { get; init; }
        public bool HasImage { get; init; }
        public int? PrepTime { get; init; }
        public bool IsAvailable { get; init; }
        public List<TakeOutCustomizationDto> Customizations { get; init; } = new();
        public List<TakeOutAddonDto> Addons { get; init; } = new();
    }

    public class TakeOutMenuModel : PageModel
    {
        private const string CacheKey = "takeout-menu";

        private readonly AppDbContext _db;
        private readonly IMemoryCache _cache;

        public TakeOutMenuModel(AppDbContext db, IMemoryCache cache)
        {
            _db = db;
            _cache = cache;
        }

        public string CategoriesJson { get; private set; } = "[]";
        public string ItemsJson { get; private set; } = "[]";

        public async Task OnGetAsync()
        {
            // ── Philippine Time ────────────────────────────────────────────
            var phTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila");
            var now = TimeOnly.FromDateTime(PhTime.Now);

            // The DB result is cached for 2 minutes. "Available right now" depends on the
            // clock, so it is computed in memory on every request, never cached.
            var data = await _cache.GetOrCreateAsync(CacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2);
                return await LoadAsync();
            });

            // Like MenuDisplay: every item is listed. Items that are switched off, or outside
            // their hours right now, are flagged so the page can grey them out.
            var items = data!.Items
                .Select(x => x.Dto with
                {
                    IsAvailable = x.IsAvailable && InWindow(x.From, x.Until, now)
                })
                .ToList();

            var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            CategoriesJson = JsonSerializer.Serialize(data.Categories, opts);
            ItemsJson = JsonSerializer.Serialize(items, opts);
        }

        // No hours set = available all day. Supports overnight windows (e.g. 22:00-02:00).
        // Equal start and end (e.g. the 00:00-00:00 default) also means "all day".
        private static bool InWindow(TimeOnly? from, TimeOnly? until, TimeOnly now)
        {
            if (from == null || until == null) return true;
            if (from == until) return true;
            return from <= until
                ? now >= from && now <= until
                : now >= from || now <= until;
        }

        // GET /TakeOut/TakeOutMenu?handler=Image&id=<guid>
        public async Task<IActionResult> OnGetImageAsync(Guid id)
        {
            var img = await _db.MenuItems
                .AsNoTracking()
                .Where(m => m.Id == id
                            && m.Category.IsActive && !m.Category.IsDeleted
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

        // Call from admin save code so edits show immediately.
        public static void InvalidateCache(IMemoryCache cache) => cache.Remove(CacheKey);

        // ── Data loading ──────────────────────────────────────────────────
        private async Task<CachedMenu> LoadAsync()
        {
            var cats = await _db.Categories
                .AsNoTracking()
                .Where(c => c.IsActive && !c.IsDeleted)
                .OrderBy(c => c.DisplayOrder)
                .Select(c => new TakeOutCategoryDto
                {
                    Id = c.Id,
                    Name = c.CategoryName,
                    Sub = c.Description ?? ""
                })
                .ToListAsync();

            // Slim projection: the ImageBase64 blob is never selected.
            // Only the category filter stays in SQL; availability is decided per request.
            var rows = await _db.MenuItems
                .AsNoTracking()
                .Where(m => m.Category.IsActive && !m.Category.IsDeleted)
                .OrderBy(m => m.Category.DisplayOrder)
                    .ThenBy(m => m.DisplayOrder)
                .Select(m => new
                {
                    m.Id,
                    m.CategoryId,
                    m.Name,
                    m.Description,
                    m.Price,
                    HasImage = m.ImageBase64 != null && m.ImageBase64 != "",
                    m.PreparationTime,
                    m.IsAvailable,
                    m.AvailableFrom,
                    m.AvailableUntil,
                    Customizations = m.MenuItemCustomizations
                        .OrderBy(mc => mc.Customization.DisplayOrder)
                        .Select(mc => new
                        {
                            mc.Customization.Id,
                            mc.Customization.Name,
                            mc.Customization.Type,
                            mc.Customization.IsRequired,
                            Options = mc.Customization.Options
                                .OrderBy(o => o.DisplayOrder)
                                .Select(o => new
                                {
                                    o.Id,
                                    o.OptionLabel,
                                    o.PriceModifier,
                                    o.IsDefault
                                }).ToList()
                        }).ToList(),
                    Addons = m.MenuItemAddons.Select(ma => new
                    {
                        ma.Addon.Id,
                        ma.Addon.Name,
                        ma.Addon.Category,
                        ma.Addon.Price
                    }).ToList()
                })
                .AsSplitQuery()
                .ToListAsync();

            var items = rows.Select(m => new CachedItem(
                m.IsAvailable,
                m.AvailableFrom,
                m.AvailableUntil,
                new TakeOutItemDto
                {
                    Id = m.Id,
                    Cat = m.CategoryId,
                    Name = m.Name,
                    Desc = m.Description ?? "",
                    Price = m.Price,
                    HasImage = m.HasImage,
                    PrepTime = m.PreparationTime,
                    Customizations = m.Customizations.Select(c => new TakeOutCustomizationDto
                    {
                        Id = c.Id,
                        Name = c.Name,
                        Type = c.Type?.ToString() ?? "",
                        Required = c.IsRequired,
                        Options = c.Options.Select(o => new TakeOutOptionDto
                        {
                            Id = o.Id,
                            Label = o.OptionLabel,
                            PriceModifier = o.PriceModifier,
                            IsDefault = o.IsDefault
                        }).ToList()
                    }).ToList(),
                    Addons = m.Addons.Select(a => new TakeOutAddonDto
                    {
                        Id = a.Id,
                        Name = a.Name,
                        Category = a.Category ?? "",
                        Price = a.Price
                    }).ToList()
                })).ToList();

            return new CachedMenu(cats, items);
        }

        private sealed record CachedItem(bool IsAvailable, TimeOnly? From, TimeOnly? Until, TakeOutItemDto Dto);
        private sealed record CachedMenu(List<TakeOutCategoryDto> Categories, List<CachedItem> Items);
    }
}
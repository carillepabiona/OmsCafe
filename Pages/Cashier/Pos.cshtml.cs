using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OmsCafe.Data;
using OmsCafe.Models;
using OmsCafe.Pages.TakeOut;   // reuses the take-out DTOs so the JSON shape is identical
using System.Text.Json;

namespace OmsCafe.Pages.Cashier
{
    // TODO: match your existing cashier protection, e.g. [Authorize(Roles = "Cashier")]
   // [Authorize]
    public class PosModel : PageModel
    {
        private readonly AppDbContext _db;
        public PosModel(AppDbContext db) => _db = db;

        public string CategoriesJson { get; private set; } = "[]";
        public string ItemsJson { get; private set; } = "[]";

        public async Task OnGetAsync()
        {
            var now = TimeOnly.FromDateTime(PhTime.Now);

            var cats = await _db.Categories.AsNoTracking()
                .Where(c => c.IsActive && !c.IsDeleted)
                .OrderBy(c => c.DisplayOrder)
                .Select(c => new TakeOutCategoryDto { Id = c.Id, Name = c.CategoryName, Sub = c.Description ?? "" })
                .ToListAsync();

            // No caching on purpose: the cashier must always see live availability.
            var rows = await _db.MenuItems.AsNoTracking()
                .Where(m => m.Category.IsActive && !m.Category.IsDeleted)
                .OrderBy(m => m.Category.DisplayOrder).ThenBy(m => m.DisplayOrder)
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
                            Options = mc.Customization.Options.OrderBy(o => o.DisplayOrder)
                                .Select(o => new { o.Id, o.OptionLabel, o.PriceModifier, o.IsDefault }).ToList()
                        }).ToList(),
                    Addons = m.MenuItemAddons
                        .Select(ma => new { ma.Addon.Id, ma.Addon.Name, ma.Addon.Category, ma.Addon.Price }).ToList()
                })
                .AsSplitQuery()
                .ToListAsync();

            var items = rows.Select(m => new TakeOutItemDto
            {
                Id = m.Id,
                Cat = m.CategoryId,
                Name = m.Name,
                Desc = m.Description ?? "",
                Price = m.Price,
                HasImage = m.HasImage,
                PrepTime = m.PreparationTime,
                IsAvailable = m.IsAvailable && InWindow(m.AvailableFrom, m.AvailableUntil, now),
                Customizations = m.Customizations.Select(c => new TakeOutCustomizationDto
                {
                    Id = c.Id,
                    Name = c.Name,
                    Type = c.Type?.ToString() ?? "",
                    Required = c.IsRequired,
                    Options = c.Options.Select(o => new TakeOutOptionDto
                    { Id = o.Id, Label = o.OptionLabel, PriceModifier = o.PriceModifier, IsDefault = o.IsDefault }).ToList()
                }).ToList(),
                Addons = m.Addons.Select(a => new TakeOutAddonDto
                { Id = a.Id, Name = a.Name, Category = a.Category ?? "", Price = a.Price }).ToList()
            }).ToList();

            var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            CategoriesJson = JsonSerializer.Serialize(cats, opts);
            ItemsJson = JsonSerializer.Serialize(items, opts);
        }

        private static bool InWindow(TimeOnly? from, TimeOnly? until, TimeOnly now)
        {
            if (from == null || until == null || from == until) return true;
            return from <= until ? now >= from && now <= until : now >= from || now <= until;
        }
    }
}

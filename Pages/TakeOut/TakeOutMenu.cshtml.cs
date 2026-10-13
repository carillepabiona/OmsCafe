using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OmsCafe.Data;
using OmsCafe.Models;
using System.Text.Json;

namespace OmsCafe.Pages.TakeOut
{
    public class TakeOutMenuModel : PageModel
    {
        private readonly AppDbContext _db;
        public TakeOutMenuModel(AppDbContext db) => _db = db;

        public string CategoriesJson { get; private set; } = "[]";
        public string ItemsJson { get; private set; } = "[]";

        public async Task OnGetAsync()
        {
            // ── Philippine Time ────────────────────────────────────────────
            var phTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila");
            var now = TimeOnly.FromDateTime(
                TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, phTimeZone)
            );

            // ── Categories ─────────────────────────────────────────────────
            var cats = await _db.Categories
                .Where(c => c.IsActive && !c.IsDeleted)
                .OrderBy(c => c.DisplayOrder)
                .Select(c => new {
                    id = c.Id,
                    name = c.CategoryName,
                    sub = c.Description ?? ""
                })
                .ToListAsync();

            // ── Menu Items ─────────────────────────────────────────────────
            var menuItems = await _db.MenuItems
                .Include(m => m.Category)
                .Include(m => m.MenuItemCustomizations)
                    .ThenInclude(mc => mc.Customization)
                        .ThenInclude(c => c.Options)
                .Include(m => m.MenuItemAddons)
                    .ThenInclude(ma => ma.Addon)
                .Where(m => m.IsAvailable
                         && m.Category.IsActive
                         && !m.Category.IsDeleted
                         && m.AvailableFrom <= now
                         && m.AvailableUntil >= now)
                .OrderBy(m => m.Category.DisplayOrder)
                    .ThenBy(m => m.DisplayOrder)
                .ToListAsync();

            // ── If nothing came back, retry without time filter ────────────
            // (safety net: surfaces items even if time data is misconfigured)
            if (!menuItems.Any())
            {
                menuItems = await _db.MenuItems
                    .Include(m => m.Category)
                    .Include(m => m.MenuItemCustomizations)
                        .ThenInclude(mc => mc.Customization)
                            .ThenInclude(c => c.Options)
                    .Include(m => m.MenuItemAddons)
                        .ThenInclude(ma => ma.Addon)
                    .Where(m => m.IsAvailable
                             && m.Category.IsActive
                             && !m.Category.IsDeleted)
                    .OrderBy(m => m.Category.DisplayOrder)
                        .ThenBy(m => m.DisplayOrder)
                    .ToListAsync();
            }

            // ── Project to DTOs ────────────────────────────────────────────
            var itemsDto = menuItems.Select(m => new {
                id = m.Id,
                cat = m.CategoryId,
                name = m.Name,
                desc = m.Description ?? "",
                price = m.Price,
                image = m.ImageBase64 != null
                    ? $"data:{m.ImageMimeType};base64,{m.ImageBase64}"
                    : (string?)null,
                prepTime = m.PreparationTime,
                customizations = m.MenuItemCustomizations
                    .OrderBy(mc => mc.Customization.DisplayOrder)
                    .Select(mc => new {
                        id = mc.Customization.Id,
                        name = mc.Customization.Name,
                        type = mc.Customization.Type,
                        required = mc.Customization.IsRequired,
                        options = mc.Customization.Options
                            .OrderBy(o => o.DisplayOrder)
                            .Select(o => new {
                                id = o.Id,
                                label = o.OptionLabel,
                                priceModifier = o.PriceModifier,
                                isDefault = o.IsDefault
                            })
                    }),
                addons = m.MenuItemAddons
                    .Select(ma => new {
                        id = ma.Addon.Id,
                        name = ma.Addon.Name,
                        category = ma.Addon.Category ?? "",
                        price = ma.Addon.Price
                    })
            }).ToList();

            var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            CategoriesJson = JsonSerializer.Serialize(cats, opts);
            ItemsJson = JsonSerializer.Serialize(itemsDto, opts);
        }
    }
}
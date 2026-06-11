using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OmsCafe.Data;
using OmsCafe.Models;

namespace OmsCafe.Pages.Admin
{
    public class MenuManagementModel : PageModel
    {
        private readonly AppDbContext _db;

        public MenuManagementModel(AppDbContext db) => _db = db;

        // ── Page State ─────────────────────────────────────────────
        [BindProperty(SupportsGet = true)]
        public string ActiveTab { get; set; } = "items";

        // ── Data ───────────────────────────────────────────────────
        public List<MenuItemViewModel> MenuItems { get; set; } = new();
        public List<CategoryViewModel> Categories { get; set; } = new();
        public List<CustomizationViewModel> Customizations { get; set; } = new();
        public List<AddonViewModel> Addons { get; set; } = new();

        // ── GET ────────────────────────────────────────────────────
        public async Task OnGetAsync(string? activeTab = "items")
        {
            ActiveTab = activeTab ?? "items";
            await LoadAllAsync();
        }

        private async Task LoadAllAsync()
        {
            Categories = await _db.Categories
                .Where(c => !c.IsDeleted)
                .OrderBy(c => c.DisplayOrder)
                .Select(c => new CategoryViewModel
                {
                    Id = c.Id,
                    CategoryName = c.CategoryName,
                    Description = c.Description,
                    Color = c.Color,
                    DisplayOrder = c.DisplayOrder,
                    IsActive = c.IsActive,
                    ItemCount = c.MenuItems.Count
                }).ToListAsync();

            // Load assigned customization & addon IDs per item
            var assignedCustomizations = await _db.MenuItemCustomizations
                .GroupBy(x => x.MenuItemId)
                .Select(g => new { ItemId = g.Key, Ids = g.Select(x => x.CustomizationId).ToList() })
                .ToListAsync();

            var assignedAddons = await _db.MenuItemAddons
                .GroupBy(x => x.MenuItemId)
                .Select(g => new { ItemId = g.Key, Ids = g.Select(x => x.AddonId).ToList() })
                .ToListAsync();

            var custDict = assignedCustomizations.ToDictionary(x => x.ItemId, x => x.Ids);
            var addonDict = assignedAddons.ToDictionary(x => x.ItemId, x => x.Ids);

            MenuItems = await _db.MenuItems
                .Include(m => m.Category)
                .OrderBy(m => m.DisplayOrder).ThenBy(m => m.Name)
                .Select(m => new MenuItemViewModel
                {
                    Id = m.Id,
                    CategoryId = m.CategoryId,
                    CategoryName = m.Category.CategoryName,
                    Name = m.Name,
                    Description = m.Description,
                    Price = m.Price,
                    Cost = m.Cost,
                    PreparationTime = m.PreparationTime,
                    IsAvailable = m.IsAvailable,
                    AvailableFrom = m.AvailableFrom.ToString(@"hh\:mm"),
                    AvailableUntil = m.AvailableUntil.ToString(@"hh\:mm"),
                    ImageBase64 = m.ImageBase64,
                    ImageMimeType = m.ImageMimeType,
                    DisplayOrder = m.DisplayOrder
                }).ToListAsync();

            // Attach assigned IDs
            foreach (var item in MenuItems)
            {
                item.AssignedCustomizationIds = custDict.TryGetValue(item.Id, out var cids) ? cids : new();
                item.AssignedAddonIds = addonDict.TryGetValue(item.Id, out var aids) ? aids : new();
            }

            Customizations = await _db.Customizations
                .Include(c => c.Options)
                .OrderBy(c => c.DisplayOrder)
                .Select(c => new CustomizationViewModel
                {
                    Id = c.Id,
                    Name = c.Name,
                    Type = c.Type,
                    IsRequired = c.IsRequired,
                    DisplayOrder = c.DisplayOrder,
                    Options = c.Options.OrderBy(o => o.DisplayOrder)
                                   .Select(o => new CustomizationOptionViewModel
                                   {
                                       Id = o.Id,
                                       OptionLabel = o.OptionLabel,
                                       PriceModifier = o.PriceModifier,
                                       IsDefault = o.IsDefault,
                                       DisplayOrder = o.DisplayOrder
                                   }).ToList()
                }).ToListAsync();

            Addons = await _db.Addons
                .OrderBy(a => a.Name)
                .Select(a => new AddonViewModel
                {
                    Id = a.Id,
                    Name = a.Name,
                    Category = a.Category,
                    Price = a.Price,
                    IsActive = a.IsActive
                }).ToListAsync();
        }


        // ── POST: Save Item Assignments ────────────────────────────
        public async Task<IActionResult> OnPostSaveItemAssignmentsAsync(
            Guid itemId,
            List<Guid>? customizationIds,
            List<Guid>? addonIds,
            string activeTab = "items")
        {
            try
            {
                // Remove existing assignments
                var existingCusts = _db.MenuItemCustomizations.Where(x => x.MenuItemId == itemId);
                var existingAddons = _db.MenuItemAddons.Where(x => x.MenuItemId == itemId);
                _db.MenuItemCustomizations.RemoveRange(existingCusts);
                _db.MenuItemAddons.RemoveRange(existingAddons);

                // Add new assignments
                if (customizationIds != null)
                    foreach (var cid in customizationIds)
                        _db.MenuItemCustomizations.Add(new MenuItemCustomization { MenuItemId = itemId, CustomizationId = cid });

                if (addonIds != null)
                    foreach (var aid in addonIds)
                        _db.MenuItemAddons.Add(new MenuItemAddon { MenuItemId = itemId, AddonId = aid });

                await _db.SaveChangesAsync();
                TempData["Success"] = "Item assignments saved.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Failed to save assignments: {ex.Message}";
            }

            return RedirectToPage(new { activeTab });
        }

        // ══════════════════════════════════════════════════════════
        // POST: Save Menu Item (Add + Edit)
        // ══════════════════════════════════════════════════════════
        public async Task<IActionResult> OnPostSaveItemAsync(
            Guid? Id,
            string Name,
            Guid CategoryId,
            string? Description,
            decimal Price,
            decimal Cost,
            int PreparationTime,
            bool IsAvailable,
            string AvailableFrom,
            string AvailableUntil,
            IFormFile? ImageFile,
            string? ExistingImageBase64,
            string? ExistingImageMimeType,
            string activeTab = "items")
        {
            try
            {
                string? imageBase64 = ExistingImageBase64;
                string? imageMime = ExistingImageMimeType;

                if (ImageFile != null && ImageFile.Length > 0)
                {
                    using var ms = new MemoryStream();
                    await ImageFile.CopyToAsync(ms);
                    imageBase64 = Convert.ToBase64String(ms.ToArray());
                    imageMime = ImageFile.ContentType;
                }

                // FIX: Parse into TimeSpan first, then convert to TimeOnly
                var fromTime = TimeSpan.TryParse(AvailableFrom, out var ft)
                    ? TimeOnly.FromTimeSpan(ft)
                    : new TimeOnly(6, 0);

                var untilTime = TimeSpan.TryParse(AvailableUntil, out var ut)
                    ? TimeOnly.FromTimeSpan(ut)
                    : new TimeOnly(22, 0);

                if (Id == null || Id == Guid.Empty)
                {
                    var item = new MenuItem
                    {
                        Id = Guid.NewGuid(),
                        CategoryId = CategoryId,
                        Name = Name,
                        Description = Description,
                        Price = Price,
                        Cost = Cost,
                        PreparationTime = PreparationTime,
                        IsAvailable = IsAvailable,
                        AvailableFrom = fromTime,
                        AvailableUntil = untilTime,
                        ImageBase64 = imageBase64,
                        ImageMimeType = imageMime,
                        DisplayOrder = 0,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _db.MenuItems.Add(item);
                }
                else
                {
                    var item = await _db.MenuItems.FindAsync(Id);
                    if (item == null) return NotFound();

                    item.CategoryId = CategoryId;
                    item.Name = Name;
                    item.Description = Description;
                    item.Price = Price;
                    item.Cost = Cost;
                    item.PreparationTime = PreparationTime;
                    item.IsAvailable = IsAvailable;
                    item.AvailableFrom = fromTime;
                    item.AvailableUntil = untilTime;
                    item.UpdatedAt = DateTime.UtcNow;

                    if (imageBase64 != null)
                    {
                        item.ImageBase64 = imageBase64;
                        item.ImageMimeType = imageMime;
                    }
                }

                await _db.SaveChangesAsync();
                TempData["Success"] = "Menu item saved successfully.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Failed to save item: {ex.Message}";
            }

            return RedirectToPage(new { activeTab });
        }

        // ══════════════════════════════════════════════════════════
        // POST: Toggle Availability
        // ══════════════════════════════════════════════════════════
        public async Task<IActionResult> OnPostToggleAvailabilityAsync(Guid id, string activeTab = "items")
        {
            var item = await _db.MenuItems.FindAsync(id);
            if (item != null)
            {
                item.IsAvailable = !item.IsAvailable;
                item.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }
            return RedirectToPage(new { activeTab });
        }

        // ══════════════════════════════════════════════════════════
        // POST: Save Availability Time
        // ══════════════════════════════════════════════════════════
        public async Task<IActionResult> OnPostSaveAvailabilityTimeAsync(
            Guid id, string? availableFrom, string? availableUntil, string activeTab = "availability")
        {
            var item = await _db.MenuItems.FindAsync(id);
            if (item != null)
            {
                // FIX: Convert TimeSpan → TimeOnly
                if (TimeSpan.TryParse(availableFrom, out var ft))
                    item.AvailableFrom = TimeOnly.FromTimeSpan(ft);

                if (TimeSpan.TryParse(availableUntil, out var ut))
                    item.AvailableUntil = TimeOnly.FromTimeSpan(ut);

                item.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }
            return RedirectToPage(new { activeTab });
        }

        // ══════════════════════════════════════════════════════════
        // POST: Delete Menu Item
        // ══════════════════════════════════════════════════════════
        public async Task<IActionResult> OnPostDeleteItemAsync(Guid id, string activeTab = "items")
        {
            var item = await _db.MenuItems.FindAsync(id);
            if (item != null)
            {
                _db.MenuItems.Remove(item);
                await _db.SaveChangesAsync();
                TempData["Success"] = "Item deleted.";
            }
            return RedirectToPage(new { activeTab });
        }

        // ══════════════════════════════════════════════════════════
        // POST: Save Category (Add + Edit)
        // ══════════════════════════════════════════════════════════
        public async Task<IActionResult> OnPostSaveCategoryAsync(
            Guid? Id,
            string CategoryName,
            string? Description,
            string Color,
            int DisplayOrder,
            bool IsActive,
            string activeTab = "categories")
        {
            try
            {
                if (Id == null || Id == Guid.Empty)
                {
                    _db.Categories.Add(new Category
                    {
                        Id = Guid.NewGuid(),
                        CategoryName = CategoryName,
                        Description = Description,
                        Color = Color,
                        DisplayOrder = DisplayOrder,
                        IsActive = IsActive,
                        IsDeleted = false,      // FIX: now valid after adding to model
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                }
                else
                {
                    var cat = await _db.Categories.FindAsync(Id);
                    if (cat == null) return NotFound();

                    cat.CategoryName = CategoryName;
                    cat.Description = Description;
                    cat.Color = Color;
                    cat.DisplayOrder = DisplayOrder;
                    cat.IsActive = IsActive;
                    cat.UpdatedAt = DateTime.UtcNow;
                }

                await _db.SaveChangesAsync();
                TempData["Success"] = "Category saved.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Failed to save category: {ex.Message}";
            }

            return RedirectToPage(new { activeTab });
        }

        // ══════════════════════════════════════════════════════════
        // POST: Delete Category
        // ══════════════════════════════════════════════════════════
        public async Task<IActionResult> OnPostDeleteCategoryAsync(Guid id, string activeTab = "categories")
        {
            var cat = await _db.Categories.FindAsync(id);
            if (cat != null)
            {
                cat.IsDeleted = true;   // soft delete — FIX: now valid after adding to model
                cat.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
                TempData["Success"] = "Category deleted.";
            }
            return RedirectToPage(new { activeTab });
        }

        // ══════════════════════════════════════════════════════════
        // POST: Save Customization (Add + Edit)
        // ══════════════════════════════════════════════════════════
        public async Task<IActionResult> OnPostSaveCustomizationAsync(
    Guid? Id,
    string Name,
    string Type,
    bool IsRequired,
    int DisplayOrder,
    List<CustomizationOptionInput>? Options,
    string activeTab = "customizations")
        {
            try
            {
                if (Id == null || Id == Guid.Empty)
                {
                    var cust = new Customization
                    {
                        Id = Guid.NewGuid(),
                        Name = Name,
                        Type = Type,
                        IsRequired = IsRequired,
                        DisplayOrder = DisplayOrder,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };

                    if (Options != null)
                        cust.Options = Options.Select((o, i) => new CustomizationOption
                        {
                            Id = Guid.NewGuid(),
                            CustomizationId = cust.Id,
                            OptionLabel = o.OptionLabel,
                            PriceModifier = o.PriceModifier,
                            IsDefault = o.IsDefault,
                            DisplayOrder = i
                        }).ToList();

                    _db.Customizations.Add(cust);
                    await _db.SaveChangesAsync();
                }
                else
                {
                    // Load parent only — no Include, no tracked children yet
                    var cust = await _db.Customizations
                        .FirstOrDefaultAsync(c => c.Id == Id);

                    if (cust == null) return NotFound();

                    // Load existing option IDs directly from DB, untracked
                    var existingOptionIds = await _db.CustomizationOptions
                        .Where(o => o.CustomizationId == Id)
                        .Select(o => o.Id)
                        .ToListAsync();

                    // Delete each option by fetching a stub entity — avoids stale tracking
                    foreach (var optId in existingOptionIds)
                    {
                        var stub = new CustomizationOption { Id = optId };
                        _db.CustomizationOptions.Attach(stub);
                        _db.CustomizationOptions.Remove(stub);
                    }

                    await _db.SaveChangesAsync(); // flush deletes cleanly

                    // Update scalar fields
                    cust.Name = Name;
                    cust.Type = Type;
                    cust.IsRequired = IsRequired;
                    cust.DisplayOrder = DisplayOrder;
                    cust.UpdatedAt = DateTime.UtcNow;

                    // Reinsert options with fresh GUIDs
                    var newOptions = Options?.Select((o, i) => new CustomizationOption
                    {
                        Id = Guid.NewGuid(),
                        CustomizationId = cust.Id,
                        OptionLabel = o.OptionLabel,
                        PriceModifier = o.PriceModifier,
                        IsDefault = o.IsDefault,
                        DisplayOrder = i
                    }).ToList() ?? new List<CustomizationOption>();

                    _db.CustomizationOptions.AddRange(newOptions);
                    await _db.SaveChangesAsync(); // flush parent update + new options
                }

                TempData["Success"] = "Customization saved.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Failed to save customization: {ex.Message}";
            }

            return RedirectToPage(new { activeTab });
        }

        // ══════════════════════════════════════════════════════════
        // POST: Delete Customization
        // ══════════════════════════════════════════════════════════
        public async Task<IActionResult> OnPostDeleteCustomizationAsync(Guid id, string activeTab = "customizations")
        {
            var cust = await _db.Customizations.Include(c => c.Options).FirstOrDefaultAsync(c => c.Id == id);
            if (cust != null)
            {
                _db.CustomizationOptions.RemoveRange(cust.Options);
                _db.Customizations.Remove(cust);
                await _db.SaveChangesAsync();
                TempData["Success"] = "Customization deleted.";
            }
            return RedirectToPage(new { activeTab });
        }

        // ══════════════════════════════════════════════════════════
        // POST: Save Addon (Add + Edit)
        // ══════════════════════════════════════════════════════════
        public async Task<IActionResult> OnPostSaveAddonAsync(
            Guid? Id,
            string Name,
            string? Category,
            decimal Price,
            bool IsActive,
            string activeTab = "addons")
        {
            try
            {
                if (Id == null || Id == Guid.Empty)
                {
                    _db.Addons.Add(new Addon
                    {
                        Id = Guid.NewGuid(),
                        Name = Name,
                        Category = Category,
                        Price = Price,
                        IsActive = IsActive,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                }
                else
                {
                    var addon = await _db.Addons.FindAsync(Id);
                    if (addon == null) return NotFound();

                    addon.Name = Name;
                    addon.Category = Category;
                    addon.Price = Price;
                    addon.IsActive = IsActive;
                    addon.UpdatedAt = DateTime.UtcNow;
                }

                await _db.SaveChangesAsync();
                TempData["Success"] = "Add-on saved.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Failed to save add-on: {ex.Message}";
            }

            return RedirectToPage(new { activeTab });
        }

        // ══════════════════════════════════════════════════════════
        // POST: Delete Addon
        // ══════════════════════════════════════════════════════════
        public async Task<IActionResult> OnPostDeleteAddonAsync(Guid id, string activeTab = "addons")
        {
            var addon = await _db.Addons.FindAsync(id);
            if (addon != null)
            {
                _db.Addons.Remove(addon);
                await _db.SaveChangesAsync();
                TempData["Success"] = "Add-on deleted.";
            }
            return RedirectToPage(new { activeTab });
        }

        // ══════════════════════════════════════════════════════════
        // POST: Save Pricing
        // ══════════════════════════════════════════════════════════
        public async Task<IActionResult> OnPostSavePricingAsync(
            Guid Id, decimal Price, decimal Cost, string activeTab = "pricing")
        {
            var item = await _db.MenuItems.FindAsync(Id);
            if (item != null)
            {
                item.Price = Price;
                item.Cost = Cost;
                item.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
                TempData["Success"] = "Pricing updated.";
            }
            return RedirectToPage(new { activeTab });
        }

        // ══════════════════════════════════════════════════════════
        // View Models
        // ══════════════════════════════════════════════════════════
        // ══ View Models ════════════════════════════════════════════
        public class MenuItemViewModel
        {
            public Guid Id { get; set; }
            public Guid CategoryId { get; set; }
            public string CategoryName { get; set; } = "";
            public string Name { get; set; } = "";
            public string? Description { get; set; }
            public decimal Price { get; set; }
            public decimal Cost { get; set; }
            public int PreparationTime { get; set; }
            public bool IsAvailable { get; set; }
            public string AvailableFrom { get; set; } = "06:00";
            public string AvailableUntil { get; set; } = "22:00";
            public string? ImageBase64 { get; set; }
            public string? ImageMimeType { get; set; }
            public int DisplayOrder { get; set; }
            // NEW
            public List<Guid> AssignedCustomizationIds { get; set; } = new();
            public List<Guid> AssignedAddonIds { get; set; } = new();
        }

        public class CategoryViewModel
        {
            public Guid Id { get; set; }
            public string CategoryName { get; set; } = "";
            public string? Description { get; set; }
            public string Color { get; set; } = "#8B4513";
            public int DisplayOrder { get; set; }
            public bool IsActive { get; set; }
            public int ItemCount { get; set; }
        }

        public class CustomizationViewModel
        {
            public Guid Id { get; set; }
            public string Name { get; set; } = "";
            public string Type { get; set; } = "Radio";
            public bool IsRequired { get; set; }
            public int DisplayOrder { get; set; }
            public List<CustomizationOptionViewModel> Options { get; set; } = new();
        }

        public class CustomizationOptionViewModel
        {
            public Guid Id { get; set; }
            public string OptionLabel { get; set; } = "";
            public decimal PriceModifier { get; set; }
            public bool IsDefault { get; set; }
            public int DisplayOrder { get; set; }
        }

        public class AddonViewModel
        {
            public Guid Id { get; set; }
            public string Name { get; set; } = "";
            public string? Category { get; set; }
            public decimal Price { get; set; }
            public bool IsActive { get; set; }
        }

        public class CustomizationOptionInput
        {
            public Guid? Id { get; set; }
            public string OptionLabel { get; set; } = "";
            public decimal PriceModifier { get; set; } = 0;
            public bool IsDefault { get; set; } = false;
            public int DisplayOrder { get; set; } = 0;
        }
    }
}
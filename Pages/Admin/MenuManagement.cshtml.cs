using System.Text.RegularExpressions;
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

        // ── Settings ───────────────────────────────────────────────
        // An item shows the "NEW" label for this many days after it was created.
        // Editing an item does NOT restart the count (only CreatedAt is used).
        public int NewItemDays { get; } = 7;

        // Server-side limits. KEEP IN SYNC with the data-max / data-min attributes in MenuManagement.cshtml.
        private const int MaxItemName = 60;
        private const int MaxItemDescription = 250;
        private const int MaxCategoryName = 40;
        private const int MaxCategoryDescription = 120;
        private const int MaxCustomizationName = 40;
        private const int MaxOptionLabel = 40;
        private const int MaxAddonName = 40;
        private const int MaxAddonCategory = 30;
        private const decimal MaxItemPrice = 99999.99m;
        private const decimal MaxAddonPrice = 9999.99m;
        private const long MaxImageBytes = 5 * 1024 * 1024;

        private static readonly Regex NamePattern =
            new(@"^[\p{L}\p{N} .,'’&()+/\-]+$", RegexOptions.Compiled);
        private static readonly Regex HexColor =
            new(@"^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);

        // ── Page State ─────────────────────────────────────────────
        [BindProperty(SupportsGet = true)]
        public string ActiveTab { get; set; } = "items";

        // ── Data ───────────────────────────────────────────────────
        public List<MenuItemViewModel> MenuItems { get; set; } = new();
        public List<CategoryViewModel> Categories { get; set; } = new();
        public List<CustomizationViewModel> Customizations { get; set; } = new();
        public List<AddonViewModel> Addons { get; set; } = new();

        // ── Validation helpers ─────────────────────────────────────
        private IActionResult Fail(string message, string tab)
        {
            TempData["Error"] = message;
            return RedirectToPage(new { activeTab = tab });
        }

        private static string? CheckName(string? value, string label, int max, bool required = true)
        {
            if (string.IsNullOrWhiteSpace(value))
                return required ? $"{label} is required." : null;
            if (value.Length > max)
                return $"{label} must be {max} characters or fewer.";
            if (!NamePattern.IsMatch(value) || !value.Any(char.IsLetterOrDigit))
                return $"{label} contains invalid characters. Use letters, numbers and basic punctuation only.";
            return null;
        }

        private static string? CheckMoney(decimal value, string label, decimal min, decimal max)
        {
            if (decimal.Round(value, 2) != value)
                return $"{label} can have at most 2 decimal places.";
            if (value < min)
                return min == 0 ? $"{label} cannot be negative." : $"{label} must be at least ₱{min:0.00}.";
            if (value > max)
                return $"{label} cannot be more than ₱{max:N2}.";
            return null;
        }

        private static string? CheckTimes(string? from, string? until, out TimeOnly fromTime, out TimeOnly untilTime)
        {
            fromTime = default;
            untilTime = default;

            if (!TimeSpan.TryParse(from, out var f) || !TimeSpan.TryParse(until, out var u))
                return "Enter a valid start and end time.";
            if (f < TimeSpan.Zero || f >= TimeSpan.FromDays(1) || u < TimeSpan.Zero || u >= TimeSpan.FromDays(1))
                return "Enter a valid start and end time.";
            if (f == u)
                return "Start and end time cannot be the same.";

            fromTime = TimeOnly.FromTimeSpan(f);
            untilTime = TimeOnly.FromTimeSpan(u);
            return null;
        }

        // ── GET: page ──────────────────────────────────────────────
        public async Task OnGetAsync(string? activeTab = "items")
        {
            ActiveTab = activeTab ?? "items";
            await LoadAllAsync();
        }

        // ── GET: image endpoint (?handler=Image&id=...) ────────────
        // Images are served here (cacheable, lazy-loaded) instead of being
        // embedded as base64 in the page HTML.
        public async Task<IActionResult> OnGetImageAsync(Guid id)
        {
            var img = await _db.MenuItems.AsNoTracking()
                .Where(m => m.Id == id && m.ImageBase64 != null)
                .Select(m => new { m.ImageBase64, m.ImageMimeType })
                .FirstOrDefaultAsync();

            if (img == null) return NotFound();

            Response.Headers.CacheControl = "private, max-age=604800"; // 7 days
            return File(Convert.FromBase64String(img.ImageBase64!), img.ImageMimeType ?? "image/jpeg");
        }

        private async Task LoadAllAsync()
        {
            Categories = await _db.Categories.AsNoTracking()
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

            // Flat queries, grouped in memory
            var custLookup = (await _db.MenuItemCustomizations.AsNoTracking()
                    .Select(x => new { x.MenuItemId, x.CustomizationId }).ToListAsync())
                .ToLookup(x => x.MenuItemId, x => x.CustomizationId);

            var addonLookup = (await _db.MenuItemAddons.AsNoTracking()
                    .Select(x => new { x.MenuItemId, x.AddonId }).ToListAsync())
                .ToLookup(x => x.MenuItemId, x => x.AddonId);

            // Items created on or after this moment get the "NEW" label
            var newSince = PhTime.Now.AddDays(-NewItemDays);

            // NOTE: image data is never loaded here, only a HasImage flag.
            // NOTE: "HH" (24-hour). "hh" on TimeOnly is the 12-hour clock and would turn 22:00 into 10:00.
            MenuItems = await _db.MenuItems.AsNoTracking()
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
                    IsAvailable = m.IsAvailable,
                    AvailableFrom = m.AvailableFrom.ToString(@"HH\:mm"),
                    AvailableUntil = m.AvailableUntil.ToString(@"HH\:mm"),
                    DisplayOrder = m.DisplayOrder,
                    HasImage = m.ImageBase64 != null,
                    IsNew = m.CreatedAt >= newSince,
                    UpdatedAt = m.UpdatedAt
                }).ToListAsync();

            foreach (var item in MenuItems)
            {
                item.AssignedCustomizationIds = custLookup[item.Id].ToList();
                item.AssignedAddonIds = addonLookup[item.Id].ToList();
            }

            Customizations = await _db.Customizations.AsNoTracking()
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

            Addons = await _db.Addons.AsNoTracking()
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
                var existingCusts = _db.MenuItemCustomizations.Where(x => x.MenuItemId == itemId);
                var existingAddons = _db.MenuItemAddons.Where(x => x.MenuItemId == itemId);
                _db.MenuItemCustomizations.RemoveRange(existingCusts);
                _db.MenuItemAddons.RemoveRange(existingAddons);

                if (customizationIds != null)
                    foreach (var cid in customizationIds.Distinct())
                        _db.MenuItemCustomizations.Add(new MenuItemCustomization { MenuItemId = itemId, CustomizationId = cid });

                if (addonIds != null)
                    foreach (var aid in addonIds.Distinct())
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

        // ── POST: Save Menu Item (Add + Edit) ──────────────────────
        // Prep time is no longer entered here. The PreparationTime column is left untouched
        // in the database, so other pages that still read it keep working.
        public async Task<IActionResult> OnPostSaveItemAsync(
            Guid? Id,
            string? Name,
            Guid CategoryId,
            string? Description,
            decimal Price,
            decimal Cost,
            bool IsAvailable,
            string? AvailableFrom,
            string? AvailableUntil,
            IFormFile? ImageFile,
            string activeTab = "items")
        {
            try
            {
                Name = Name?.Trim();
                Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();

                // ── validation ──
                var error =
                    CheckName(Name, "Item name", MaxItemName)
                    ?? (CategoryId == Guid.Empty ? "Please select a category." : null)
                    ?? (Description != null && Description.Length > MaxItemDescription
                            ? $"Description must be {MaxItemDescription} characters or fewer." : null)
                    ?? CheckMoney(Price, "Price", 0.01m, MaxItemPrice)
                    ?? CheckMoney(Cost, "Cost", 0m, MaxItemPrice);
                if (error != null) return Fail(error, activeTab);

                error = CheckTimes(AvailableFrom, AvailableUntil, out var fromTime, out var untilTime);
                if (error != null) return Fail(error, activeTab);

                if (!await _db.Categories.AnyAsync(c => c.Id == CategoryId && !c.IsDeleted))
                    return Fail("The selected category no longer exists.", activeTab);

                var selfId = Id ?? Guid.Empty;
                var lowered = Name!.ToLower();
                if (await _db.MenuItems.AnyAsync(m =>
                        m.Id != selfId && m.CategoryId == CategoryId && m.Name.ToLower() == lowered))
                    return Fail($"An item named “{Name}” already exists in this category.", activeTab);

                // Only set when a NEW image is uploaded; otherwise the existing image is left untouched.
                string? imageBase64 = null;
                string? imageMime = null;

                if (ImageFile != null && ImageFile.Length > 0)
                {
                    if (string.IsNullOrEmpty(ImageFile.ContentType) ||
                        !ImageFile.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                        return Fail("Please choose an image file (JPG, PNG, WEBP or GIF).", activeTab);

                    if (ImageFile.Length > MaxImageBytes)
                        return Fail("Image is too large. Maximum size is 5 MB.", activeTab);

                    using var ms = new MemoryStream();
                    await ImageFile.CopyToAsync(ms);
                    imageBase64 = Convert.ToBase64String(ms.ToArray());
                    imageMime = ImageFile.ContentType;
                }

                if (Id == null || Id == Guid.Empty)
                {
                    // CreatedAt is what makes the item show the "NEW" label
                    _db.MenuItems.Add(new MenuItem
                    {
                        Id = Guid.NewGuid(),
                        CategoryId = CategoryId,
                        Name = Name,
                        Description = Description,
                        Price = Price,
                        Cost = Cost,
                        IsAvailable = IsAvailable,
                        AvailableFrom = fromTime,
                        AvailableUntil = untilTime,
                        ImageBase64 = imageBase64,
                        ImageMimeType = imageMime,
                        DisplayOrder = 0,
                        CreatedAt = PhTime.Now,
                        UpdatedAt = PhTime.Now
                    });
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
                    item.IsAvailable = IsAvailable;
                    item.AvailableFrom = fromTime;
                    item.AvailableUntil = untilTime;
                    item.UpdatedAt = PhTime.Now;      // CreatedAt is not touched, so editing never re-adds "NEW"

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

        // ── POST: Toggle Availability ──────────────────────────────
        public async Task<IActionResult> OnPostToggleAvailabilityAsync(Guid id, string activeTab = "items")
        {
            var item = await _db.MenuItems.FindAsync(id);
            if (item != null)
            {
                item.IsAvailable = !item.IsAvailable;
                item.UpdatedAt = PhTime.Now;
                await _db.SaveChangesAsync();
            }
            return RedirectToPage(new { activeTab });
        }

        // ── POST: Save Availability Time (single item) ─────────────
        public async Task<IActionResult> OnPostSaveAvailabilityTimeAsync(
            Guid id, string? availableFrom, string? availableUntil, string activeTab = "availability")
        {
            var error = CheckTimes(availableFrom, availableUntil, out var fromTime, out var untilTime);
            if (error != null) return Fail(error, activeTab);

            var item = await _db.MenuItems.FindAsync(id);
            if (item != null)
            {
                item.AvailableFrom = fromTime;
                item.AvailableUntil = untilTime;
                item.UpdatedAt = PhTime.Now;
                await _db.SaveChangesAsync();
            }
            return RedirectToPage(new { activeTab });
        }

        // ── POST: Bulk Availability (multi-select) ─────────────────
        // mode = "time"        -> set availableFrom/availableUntil on every selected item
        //        "available"   -> mark every selected item available
        //        "unavailable" -> mark every selected item unavailable
        public async Task<IActionResult> OnPostSaveBulkAvailabilityAsync(
            List<Guid>? ids,
            string? mode,
            string? availableFrom,
            string? availableUntil,
            string activeTab = "availability")
        {
            // Never trust the client: re-check everything here.
            var selected = ids?.Where(i => i != Guid.Empty).Distinct().ToList() ?? new List<Guid>();
            if (selected.Count == 0)
                return Fail("Select at least one item first.", activeTab);

            if (mode is not ("time" or "available" or "unavailable"))
                return Fail("Unknown action.", activeTab);

            TimeOnly fromTime = default, untilTime = default;
            if (mode == "time")
            {
                var timeError = CheckTimes(availableFrom, availableUntil, out fromTime, out untilTime);
                if (timeError != null) return Fail(timeError, activeTab);
            }

            try
            {
                var items = await _db.MenuItems.Where(m => selected.Contains(m.Id)).ToListAsync();
                if (items.Count == 0)
                    return Fail("The selected items no longer exist.", activeTab);

                foreach (var item in items)
                {
                    switch (mode)
                    {
                        case "time":
                            item.AvailableFrom = fromTime;
                            item.AvailableUntil = untilTime;
                            break;
                        case "available":
                            item.IsAvailable = true;
                            break;
                        case "unavailable":
                            item.IsAvailable = false;
                            break;
                    }
                    item.UpdatedAt = PhTime.Now;
                }

                await _db.SaveChangesAsync();

                TempData["Success"] = mode == "time"
                    ? $"Availability time updated for {items.Count} item(s)."
                    : $"{items.Count} item(s) marked {(mode == "available" ? "available" : "unavailable")}.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Failed to update availability: {ex.Message}";
            }

            return RedirectToPage(new { activeTab });
        }

        // ── POST: Delete Menu Item ─────────────────────────────────
        public async Task<IActionResult> OnPostDeleteItemAsync(Guid id, string activeTab = "items")
        {
            try
            {
                var item = await _db.MenuItems.FindAsync(id);
                if (item != null)
                {
                    _db.MenuItems.Remove(item);
                    await _db.SaveChangesAsync();
                    TempData["Success"] = "Item deleted.";
                }
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Failed to delete item: {ex.InnerException?.Message ?? ex.Message}";
            }
            return RedirectToPage(new { activeTab });
        }

        // ── POST: Save Category (Add + Edit) ───────────────────────
        public async Task<IActionResult> OnPostSaveCategoryAsync(
            Guid? Id,
            string? CategoryName,
            string? Description,
            string? Color,
            int DisplayOrder,
            bool IsActive,
            string activeTab = "categories")
        {
            try
            {
                CategoryName = CategoryName?.Trim();
                Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();
                Color = string.IsNullOrWhiteSpace(Color) ? "#8B4513" : Color.Trim();

                var error =
                    CheckName(CategoryName, "Category name", MaxCategoryName)
                    ?? (Description != null && Description.Length > MaxCategoryDescription
                            ? $"Description must be {MaxCategoryDescription} characters or fewer." : null)
                    ?? (!HexColor.IsMatch(Color) ? "Pick a valid color." : null)
                    ?? (DisplayOrder < 0 || DisplayOrder > 999 ? "Display order must be between 0 and 999." : null);
                if (error != null) return Fail(error, activeTab);

                var selfId = Id ?? Guid.Empty;
                var lowered = CategoryName!.ToLower();
                if (await _db.Categories.AnyAsync(c =>
                        !c.IsDeleted && c.Id != selfId && c.CategoryName.ToLower() == lowered))
                    return Fail($"Category “{CategoryName}” already exists.", activeTab);

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
                        IsDeleted = false,
                        CreatedAt = PhTime.Now,
                        UpdatedAt = PhTime.Now
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
                    cat.UpdatedAt = PhTime.Now;
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

        // ── POST: Delete Category (soft delete) ────────────────────
        public async Task<IActionResult> OnPostDeleteCategoryAsync(Guid id, string activeTab = "categories")
        {
            var cat = await _db.Categories.FindAsync(id);
            if (cat != null)
            {
                cat.IsDeleted = true;
                cat.UpdatedAt = PhTime.Now;
                await _db.SaveChangesAsync();
                TempData["Success"] = "Category deleted.";
            }
            return RedirectToPage(new { activeTab });
        }

        // ── POST: Save Customization (Add + Edit) ──────────────────
        public async Task<IActionResult> OnPostSaveCustomizationAsync(
            Guid? Id,
            string? Name,
            string? Type,
            bool IsRequired,
            int DisplayOrder,
            List<CustomizationOptionInput>? Options,
            string activeTab = "customizations")
        {
            try
            {
                Name = Name?.Trim();

                // Clean the options first: trim labels, drop completely empty rows
                var cleanOptions = (Options ?? new List<CustomizationOptionInput>())
                    .Where(o => !string.IsNullOrWhiteSpace(o.OptionLabel))
                    .Select(o => new CustomizationOptionInput
                    {
                        Id = o.Id,
                        OptionLabel = o.OptionLabel.Trim(),
                        PriceModifier = o.PriceModifier,
                        IsDefault = o.IsDefault
                    }).ToList();

                var error =
                    CheckName(Name, "Customization name", MaxCustomizationName)
                    ?? (Type is "Radio" or "Checkbox" ? null : "Choose a valid customization type.")
                    ?? (cleanOptions.Count == 0 ? "Add at least one option for this customization." : null);

                if (error == null)
                {
                    foreach (var o in cleanOptions)
                    {
                        error = CheckName(o.OptionLabel, "Option label", MaxOptionLabel)
                                ?? CheckMoney(o.PriceModifier, "Price modifier", 0m, MaxAddonPrice);
                        if (error != null) break;
                    }
                }

                if (error == null && cleanOptions
                        .GroupBy(o => o.OptionLabel, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
                    error = "Option labels must be unique.";

                if (error == null && Type == "Radio" && cleanOptions.Count(o => o.IsDefault) > 1)
                    error = "A single-choice (Radio) customization can only have one default option.";

                if (error != null) return Fail(error, activeTab);

                var selfId = Id ?? Guid.Empty;
                var lowered = Name!.ToLower();
                if (await _db.Customizations.AnyAsync(c => c.Id != selfId && c.Name.ToLower() == lowered))
                    return Fail($"Customization “{Name}” already exists.", activeTab);

                if (Id == null || Id == Guid.Empty)
                {
                    var cust = new Customization
                    {
                        Id = Guid.NewGuid(),
                        Name = Name,
                        Type = Type!,
                        IsRequired = IsRequired,
                        DisplayOrder = DisplayOrder,
                        CreatedAt = PhTime.Now,
                        UpdatedAt = PhTime.Now
                    };

                    cust.Options = cleanOptions.Select((o, i) => new CustomizationOption
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
                    var cust = await _db.Customizations.FirstOrDefaultAsync(c => c.Id == Id);
                    if (cust == null) return NotFound();

                    var existingOptionIds = await _db.CustomizationOptions
                        .Where(o => o.CustomizationId == Id)
                        .Select(o => o.Id)
                        .ToListAsync();

                    foreach (var optId in existingOptionIds)
                    {
                        var stub = new CustomizationOption { Id = optId };
                        _db.CustomizationOptions.Attach(stub);
                        _db.CustomizationOptions.Remove(stub);
                    }

                    await _db.SaveChangesAsync();

                    cust.Name = Name;
                    cust.Type = Type!;
                    cust.IsRequired = IsRequired;
                    cust.DisplayOrder = DisplayOrder;
                    cust.UpdatedAt = PhTime.Now;

                    var newOptions = cleanOptions.Select((o, i) => new CustomizationOption
                    {
                        Id = Guid.NewGuid(),
                        CustomizationId = cust.Id,
                        OptionLabel = o.OptionLabel,
                        PriceModifier = o.PriceModifier,
                        IsDefault = o.IsDefault,
                        DisplayOrder = i
                    }).ToList();

                    _db.CustomizationOptions.AddRange(newOptions);
                    await _db.SaveChangesAsync();
                }

                TempData["Success"] = "Customization saved.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Failed to save customization: {ex.Message}";
            }

            return RedirectToPage(new { activeTab });
        }

        // ── POST: Delete Customization ─────────────────────────────
        public async Task<IActionResult> OnPostDeleteCustomizationAsync(Guid id, string activeTab = "customizations")
        {
            try
            {
                var cust = await _db.Customizations.Include(c => c.Options).FirstOrDefaultAsync(c => c.Id == id);
                if (cust != null)
                {
                    _db.CustomizationOptions.RemoveRange(cust.Options);
                    _db.Customizations.Remove(cust);
                    await _db.SaveChangesAsync();
                    TempData["Success"] = "Customization deleted.";
                }
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Failed to delete customization: {ex.InnerException?.Message ?? ex.Message}";
            }
            return RedirectToPage(new { activeTab });
        }

        // ── POST: Save Addon (Add + Edit) ──────────────────────────
        public async Task<IActionResult> OnPostSaveAddonAsync(
            Guid? Id,
            string? Name,
            string? Category,
            decimal Price,
            bool IsActive,
            string activeTab = "addons")
        {
            try
            {
                Name = Name?.Trim();
                Category = string.IsNullOrWhiteSpace(Category) ? null : Category.Trim();

                var error =
                    CheckName(Name, "Add-on name", MaxAddonName)
                    ?? CheckName(Category, "Add-on category", MaxAddonCategory, required: false)
                    ?? CheckMoney(Price, "Price", 0m, MaxAddonPrice);
                if (error != null) return Fail(error, activeTab);

                var selfId = Id ?? Guid.Empty;
                var lowered = Name!.ToLower();
                if (await _db.Addons.AnyAsync(a => a.Id != selfId && a.Name.ToLower() == lowered))
                    return Fail($"Add-on “{Name}” already exists.", activeTab);

                if (Id == null || Id == Guid.Empty)
                {
                    _db.Addons.Add(new Addon
                    {
                        Id = Guid.NewGuid(),
                        Name = Name,
                        Category = Category,
                        Price = Price,
                        IsActive = IsActive,
                        CreatedAt = PhTime.Now,
                        UpdatedAt = PhTime.Now
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
                    addon.UpdatedAt = PhTime.Now;
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

        // ── POST: Delete Addon ─────────────────────────────────────
        public async Task<IActionResult> OnPostDeleteAddonAsync(Guid id, string activeTab = "addons")
        {
            try
            {
                var addon = await _db.Addons.FindAsync(id);
                if (addon != null)
                {
                    _db.Addons.Remove(addon);
                    await _db.SaveChangesAsync();
                    TempData["Success"] = "Add-on deleted.";
                }
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Failed to delete add-on: {ex.InnerException?.Message ?? ex.Message}";
            }
            return RedirectToPage(new { activeTab });
        }

        // ── POST: Save Pricing ─────────────────────────────────────
        public async Task<IActionResult> OnPostSavePricingAsync(
            Guid Id, decimal Price, decimal Cost, string activeTab = "pricing")
        {
            var error =
                CheckMoney(Price, "Selling price", 0.01m, MaxItemPrice)
                ?? CheckMoney(Cost, "Cost", 0m, MaxItemPrice);
            if (error != null) return Fail(error, activeTab);

            var item = await _db.MenuItems.FindAsync(Id);
            if (item != null)
            {
                item.Price = Price;
                item.Cost = Cost;
                item.UpdatedAt = PhTime.Now;
                await _db.SaveChangesAsync();
                TempData["Success"] = "Pricing updated.";
            }
            return RedirectToPage(new { activeTab });
        }

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
            public bool IsAvailable { get; set; }
            public string AvailableFrom { get; set; } = "06:00";
            public string AvailableUntil { get; set; } = "22:00";
            public int DisplayOrder { get; set; }
            public bool HasImage { get; set; }
            public bool IsNew { get; set; }               // created within the last NewItemDays days
            public DateTime UpdatedAt { get; set; }
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